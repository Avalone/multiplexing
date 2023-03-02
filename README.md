# Multiplexing
## Решение тестового задания.

Для решения был выбран сложный вариант задания.<br />
После рассмотрения возможных вариантов решения был выбран класс TransformBlock TPL Dataflow, с помощью которого была организована очередь заданий. Это решение не идеально вписывается в рамки задачи, но решает большинство возникающих проблем штатными средствами. <br /> 
Преимущество TransformBlock в том, что он работает с Task'ами, в отличие от Channel, который оперирует ValueTask'ами, имеет потенциально лучшую производительность и расширяемость по сравнению с  решением на основе BlockingCollection. Для исключения возможных проблем с дедлоками было решено использоать строгую очерёдность заданий, что TransformBlock легко позволяет реализовать. <br /> 
Таймаут работы задания реализован для уже запущенного задания: время, которое задание проведёт в очереди, не учитывается. Но если есть необходимость сделать именно таймаут с ожиданием в очереди, он легко реализуется при помощи передачи параметра timeout в метод TransformBlock.ReceiveAsync:
```
var transformResult = await _requestQueue.ReceiveAsync(_requestTimeout, cancellationToken);
```
Основной класс решения можно найти [тут](https://github.com/Avalone/multiplexing/blob/master/src/Multiplexing.RequestProcessor/ComplexRequestProcessor.cs)

## Формулировка задания
```
namespace Interview;

/*
 * Наше приложение общается с удаленным сервисом: шлет запросы и получает ответы. С удаленным сервером
 * установлено единственное соединение, по которому мы шлем запросы и получаем ответы. Каждый запрос содержит Id (GUID),
 * ответ на запрос содержит его же. Ответы на запросы могут приходить в произвольном порядке и с произвольными задержками.
 * Нам необходимо реализовать интерфейс, который абстрагирует факт такого мультиплексирования.
 * Реализация `IRequestProcessor.SendAsync` обязана быть потокобезопасной.
 *
 *  У нас есть готовая реализация интерфейсов `ILowLevelNetworkAdapter` и `IHighLevelNetworkAdapter`
 */

// запрос, остальные поля не интересны
public sealed record Request(Guid Id);

// ответ, остальные поля не интересны
public sealed record Response(Guid Id); 

// низкоуровневый адаптер, можно делать одновременный вызов ReadAsync и WriteAsync
// можно считать это абстракцией над полнодуплексным сокетом
public interface ILowLevelNetworkAdapter
{
    // вычитывает очередной ответ, нельзя делать несколько одновременных вызовов ReadAsync
    Task<Response> ReadAsync(CancellationToken cancellationToken);

    // отправляет запрос, нельзя делать несколько одновременных вызовов WriteAsync
    Task WriteAsync(Request request, CancellationToken cancellationToken);
}

// высокоуровневый адаптер с внутренней очередью отправки сообщений, наподобие клиента RabbitMQ или Kafka
public interface IHighLevelNetworkAdapter
{
    // ставит очередной запрос в очередь на отправку. false - очередь переполнена, запрос не будет отправлен
    bool TryEnqueueWrite(Request request, CancellationToken cancellationToken);

    // вычитывает очередной ответ, нельзя делать несколько одновременных вызовов ReadAsync
    Task<Response> ReadAsync(CancellationToken cancellationToken);
}

// интерфейс, который надо реализовать
public interface IRequestProcessor
{
    // Запускает обработчик, возвращаемый Task завершается после окончания инициализации
    // гарантированно вызывается 1 раз при инициализации приложения
    Task StartAsync(CancellationToken cancellationToken);

    // выполняет мягкую остановку, т.е. завершается после завершения обработки всех запросов
    // гарантированно вызывается 1 раз при остановке приложения
    Task StopAsync(CancellationToken cancellationToken);

    // выполняет запрос, этот метод будет вызываться в приложении множеством потоков одновременно
    // При отмене CancellationToken не обязательно гарантировать то, что мы не отправим запрос на сервер, но клиент должен получить отмену задачи
    Task<Response> SendAsync(Request request, CancellationToken cancellationToken);
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////

// простой вариант задачи:
// 1. Можно пользоваться IHighLevelNetworkAdapter, т.е. переложить очередь отправки на NetworkAdapter
// 2. Можно не реализовывать обработку `CancellationToken` в методах `IRequestProcessor`
// 3. Можно не реализовывать `IRequestProcessor.StopAsync`
public sealed class SimpleRequestProcessor : IRequestProcessor
{
    private readonly IHighLevelNetworkAdapter _networkAdapter;

    public SimpleRequestProcessor(IHighLevelNetworkAdapter networkAdapter)
    {
        _networkAdapter = networkAdapter;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // cancellationToken можно не использовать
        throw new NotImplementedException();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // этот метод можно не реализовывать
        return Task.CompletedTask;
    }

    public Task<Response> SendAsync(Request request, CancellationToken cancellationToken)
    {
        // cancellationToken можно не использовать
        throw new NotImplementedException();
    }
}

// средний вариант задачи:
// 1. можно пользоваться только ILowLevelNetworkAdapter
// 2. нужно реализовать обработку cancellationToken
// 3. можно не реализовывать `IRequestProcessor.StopAsync`
public sealed class MediumRequestProcessor : IRequestProcessor
{
    private readonly ILowLevelNetworkAdapter _networkAdapter;

    public MediumRequestProcessor(ILowLevelNetworkAdapter networkAdapter)
    {
        _networkAdapter = networkAdapter;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // этот метод можно не реализовывать
        return Task.CompletedTask;
    }

    public Task<Response> SendAsync(Request request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}

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

    public ComplexRequestProcessor(ILowLevelNetworkAdapter networkAdapter, TimeSpan requestTimeout)
    {
        if (requestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));

        _networkAdapter = networkAdapter;
        _requestTimeout = requestTimeout;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<Response> SendAsync(Request request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}

```
