namespace Multiplexing.RequestProcessor;

// запрос, остальные поля не интересны
public sealed record Request(Guid Id);

// ответ, остальные поля не интересны
public sealed record Response(Guid Id);
