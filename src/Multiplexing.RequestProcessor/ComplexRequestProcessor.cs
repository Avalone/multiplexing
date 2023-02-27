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

    private record RequestWrapper(Request Request, CancellationToken Token);
    record TransformResult<T>(T Result, ExceptionDispatchInfo? Error);
    private TransformBlock<RequestWrapper, TransformResult<Response?>>? _requestQueue = null;

    public ComplexRequestProcessor(ILowLevelNetworkAdapter networkAdapter, TimeSpan requestTimeout)
    {
        if (requestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));

        _networkAdapter = networkAdapter;
        _requestTimeout = requestTimeout;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            if (_requestQueue is not null)
            {
                await StopAsync(cancellationToken);
            }
            ExecutionDataflowBlockOptions _options = new()
            {
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
                //SingleProducerConstrained = true
            };
            _requestQueue = new TransformBlock<RequestWrapper, TransformResult<Response?>>(async (wrapper) =>
            {
                Debug.WriteLine($"Preparing request: {wrapper.Request.Id}");
                try
                {
                    var cts = new CancellationTokenSource(_requestTimeout);
                    // кажется, что для полноценной реализации работы с полнодуплексным сокетом
                    // модель данных должна быть немного сложнее
                    // в данном случае считаем что апаптер сам позаботится о правильном порядке сообщений
                    var writeTask = _networkAdapter.WriteAsync(wrapper.Request, cts.Token);
                    var readTask = _networkAdapter.ReadAsync(cts.Token);
                    var bothTask = Task.WhenAll(writeTask, readTask);
                    await Task.WhenAny(
                        bothTask,
                        Task.Delay(_requestTimeout, wrapper.Token)
                    );
                    if (!bothTask.IsCompleted)
                    {
                        // согласно заданию мы должны отменять таск,
                        // иначе можно было бы выбрасывать TimeoutException
                        return new TransformResult<Response?>(null, ExceptionDispatchInfo.Capture(new OperationCanceledException("Timeout expired")));
                    }
                    Debug.WriteLine($"Request: {wrapper.Request.Id} returned response {readTask.Result.Id}");
                    return new TransformResult<Response?>(readTask.Result, null);
                }
                catch (OperationCanceledException ex)
                {
                    if (ex.CancellationToken.Equals(wrapper.Token))
                    {
                        Debug.WriteLine($"Request {wrapper.Request.Id} has been cancelled");
                        return new TransformResult<Response?>(null, ExceptionDispatchInfo.Capture(ex));
                    }
                    Debug.WriteLine($"Request {wrapper.Request.Id} reached it's timeout");
                    return new TransformResult<Response?>(null, ExceptionDispatchInfo.Capture(new TimeoutException("Timeout expired")));
                }
                catch (AggregateException ex)
                {
                    return new TransformResult<Response?>(null, ExceptionDispatchInfo.Capture(ex.InnerExceptions.First()));
                }
            }, _options);
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            if (_requestQueue is null)
            {
                // Здесь и далее выбрана стратегия "громкого падения", поскольку
                // в нормальной ситуации метод не должен быть вызван до инициализации
                // Но можно и ограничиться возвратом Task.CompletedTask, а в SendAsync вызывать StartAsync
                throw new InvalidOperationException("Stop method called before Start");
            }
            _requestQueue.Complete();
            await _requestQueue.Completion;
        }, cancellationToken);
    }

    public async Task<Response> SendAsync(Request request, CancellationToken cancellationToken)
    {
        if (_requestQueue is null)
        {
            throw new InvalidOperationException("Send method called before Start");
        }
        _requestQueue.Post(new RequestWrapper(request, cancellationToken));
        var transformResult = await _requestQueue.ReceiveAsync(cancellationToken);
        if (transformResult.Error != null)
            transformResult.Error.Throw();
        return transformResult.Result;
    }
}
