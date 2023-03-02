using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;

namespace Multiplexing.RequestProcessor;

// сложный вариант задачи:
// 1. можно пользоваться только ILowLevelNetworkAdapter
// 2. нужно реализовать обработку cancellationToken
// 3. нужно реализовать StopAsync, который дожидается получения ответов на уже переданные
//    запросы (пока не отменен переданный в `StopAsync` `CancellationToken`)
// 4. нужно реализовать настраиваемый таймаут: если ответ на конкретный запрос не получен за заданный промежуток
//    времени - отменяем задачу, которая вернулась из `SendAsync`. В том числе надо рассмотреть ситуацию,
//    что ответ на запрос не придет никогда, глобальный таймаут при этом должен отработать и не допустить утечки памяти
public sealed class ComplexRequestProcessor : IRequestProcessor
{
    private readonly ILowLevelNetworkAdapter _networkAdapter;
    private readonly TimeSpan _requestTimeout;

    private const int COLLECTIONS_CAPACITY = 100000;
    private const int ADAPTER_MAX_PARALLEL = 1;
    //private readonly TimeSpan MESSAGE_TTL = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<Guid, Response?> _buffer;
    record TransformResult<T>(T Result, ExceptionDispatchInfo? Error);
    private TransformBlock<(Request, CancellationToken), TransformResult<Response?>>? _readBlock = null;
    private ActionBlock<(Request, CancellationToken)>? _writeBlock = null;
    private BroadcastBlock<(Request, CancellationToken)>? _broadcastBlock = null;


    public ComplexRequestProcessor(ILowLevelNetworkAdapter networkAdapter, TimeSpan requestTimeout)
    {
        if (requestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));

        _networkAdapter = networkAdapter;
        _requestTimeout = requestTimeout;
        _buffer = new ConcurrentDictionary<Guid, Response?>();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            if (_readBlock is not null)
            {
                await StopAsync(cancellationToken);
            }

            var blockOptions = new ExecutionDataflowBlockOptions()
            {
                MaxDegreeOfParallelism = ADAPTER_MAX_PARALLEL,
                BoundedCapacity = COLLECTIONS_CAPACITY,
            };

            // Блок для чтения информации из адаптера
            // В цикле вычитывает ответы до тех пор, пока не получит ответ с нужным Id, либо не сработает таймаут операции,
            // либо не будет превышено максимальное число попыток, если таймаут слишком большой
            _readBlock = new TransformBlock<(Request, CancellationToken), TransformResult<Response?>>(async message =>
            {
                (Request request, CancellationToken token) = message;
                var response = _buffer[request.Id];
                var _readCounter = 0;
                try
                {
                    // можно ввести ограничение на количество попыток чтения, но тогда таймаут может отрабатывать некорректно
                    while (response is null || response.Id != request.Id)
                    {
                        token.ThrowIfCancellationRequested();
                        Interlocked.Increment(ref _readCounter);
                        Debug.WriteLine($"Read {_readCounter} started: {request.Id}");
                        response = await _networkAdapter.ReadAsync(token);
                        Debug.WriteLine($"Read {_readCounter} completed: {response.Id}");
                        if (_buffer.ContainsKey(response.Id) && _buffer[response.Id] == null)
                        {
                            _buffer[response.Id] = response;
                        }
                    }
                    
                }
                // ловим как OperationCanceledException, так и остальные ошибки чтобы обработать
                // или бросить дальше в методе, получающем результат чтения
                catch (Exception ex)
                {
                    return new TransformResult<Response?>(null, ExceptionDispatchInfo.Capture(ex));
                }
                return new TransformResult<Response?>(response, null);
            }, blockOptions);

            // Блок для записи информации в адаптер
            _writeBlock = new ActionBlock<(Request, CancellationToken)>(async (message) =>
            {
                (Request request, CancellationToken token) = message;
                Debug.WriteLine($"Write started: {request.Id}");
                try
                {
                    await _networkAdapter.WriteAsync(request, token);
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"Write completed with cancellation");
                }
                // ошибка при однократной записи не должна рушить всю очередь
                // но логику обработки, конечно, нужно расширять
                catch (Exception ex)
                {
                    Debug.WriteLine($"Write completed with error: {ex.Message}");
                }
                Debug.WriteLine($"Write completed");
            }, blockOptions);

            // Блок, который формирует начальную очередь сообщений и рассылает их дальше по пайплайну
            // параллельно в блоки чтения и записи
            _broadcastBlock = new BroadcastBlock<(Request, CancellationToken)>((message) => message, new DataflowBlockOptions { EnsureOrdered = true });
            var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
            _broadcastBlock.LinkTo(_writeBlock, linkOptions);
            _broadcastBlock.LinkTo(_readBlock, linkOptions);

        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            if (_readBlock is null || _writeBlock is null || _broadcastBlock is null)
            {
                throw new InvalidOperationException("Stop method called before Start");
            }
            _broadcastBlock.Complete();
            _writeBlock.Complete();
            _readBlock.Complete();
            await Task.WhenAll(_writeBlock.Completion, _readBlock.Completion);
            _buffer.Clear();
        }, cancellationToken);
    }

    public async Task<Response> SendAsync(Request request, CancellationToken cancellationToken)
    {
        if (_readBlock is null || _writeBlock is null || _broadcastBlock is null)
        {
            throw new InvalidOperationException("Send method called before Start");
        }
        using var timeoutCts = new CancellationTokenSource(_requestTimeout);
        var timeoutToken = timeoutCts.Token;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutToken);
        var message = (request, linkedCts.Token);
        _buffer[request.Id] = null;
        await _broadcastBlock.SendAsync(message);
        var res = await _readBlock.ReceiveAsync(cancellationToken);
        if (res.Error is not null)
        {
            Debug.WriteLine($"Request {request.Id} failed: {res.Error.SourceException.Message}");
            res.Error.Throw();
        }
        Debug.WriteLine($"Method SendAsync returned response {res.Result!.Id} on request {request.Id}");
        //if (!_buffer.Remove(request.Id, out var bufferedValue) || bufferedValue?.Id != res.Result.Id)
        //{
        //    throw new InvalidOperationException("Response validation failed");
        //}
        return res.Result;
    }
}
