namespace Multiplexing.RequestProcessor;

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
