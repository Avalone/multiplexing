using System.Collections.Concurrent;

namespace Multiplexing.RequestProcessor.Tests.Mock
{
    internal sealed class FakeLowLevelNetworkAdapter : ILowLevelNetworkAdapter
    {
        private readonly ConcurrentQueue<Request> _requests = new();

        private int _requestDelay;
        private int _responseDelay;
        private int _writeCallsCounter = 0;
        private int _readCallsCounter = 0;

        public FakeLowLevelNetworkAdapter(int requestDelay, int responseDelay)
        {
            _requestDelay = requestDelay;
            _responseDelay = responseDelay;
        }

        public int RequestDelay { get => _requestDelay; set => Interlocked.Exchange(ref _requestDelay, value); }
        public int ResponseDelay { get => _responseDelay; set => Interlocked.Exchange(ref _responseDelay, value); }

        public async Task<Response> ReadAsync(CancellationToken cancellationToken)
        {
            if (_readCallsCounter > 0)
                throw new ConcurrentAccessException("Concurrent read detected");
            Interlocked.Increment(ref _readCallsCounter);
            try
            {
                await Task.Delay(_responseDelay, cancellationToken);
                return new Response(_requests.TryDequeue(out var result) ? result.Id : Guid.Empty);
            }
            finally
            {
                Interlocked.Decrement(ref _readCallsCounter);
            }
        }

        public async Task WriteAsync(Request request, CancellationToken cancellationToken)
        {
            try
            {
                if (_writeCallsCounter > 0)
                    throw new ConcurrentAccessException("Concurrent write detected");
                Interlocked.Increment(ref _writeCallsCounter);
                await Task.Delay(_requestDelay, cancellationToken);
                _requests.Enqueue(request);
            }
            finally
            {
                Interlocked.Decrement(ref _writeCallsCounter);
            }
        }
    }
}
