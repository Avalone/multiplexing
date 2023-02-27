namespace Multiplexing.RequestProcessor;

// низкоуровневый адаптер, можно делать одновременный вызов ReadAsync и WriteAsync
// можно считать это абстракцией над полнодуплексным сокетом
public interface ILowLevelNetworkAdapter
{
    // вычитывает очередной ответ, нельзя делать несколько одновременных вызовов ReadAsync
    Task<Response> ReadAsync(CancellationToken cancellationToken);

    // отправляет запрос, нельзя делать несколько одновременных вызовов WriteAsync
    Task WriteAsync(Request request, CancellationToken cancellationToken);
}