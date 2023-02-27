namespace Multiplexing.RequestProcessor.Tests.Mock
{
    internal class ConcurrentAccessException: SystemException
    {
        public ConcurrentAccessException(string message): base(message) { }
    }
}
