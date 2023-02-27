using System.Threading;

namespace Multiplexing.RequestProcessor.Tests.Mock
{
    internal sealed class FakeLowLevelNetworkAdapter : ILowLevelNetworkAdapter
    {
        private Guid? _requestId = null;
        private int _requestDelay;
        private int _responseDelay;
        private int _writeCallsCounter = 0;
        private int _readCallsCounter = 0;

        private SemaphoreSlim _semaphore;

        public FakeLowLevelNetworkAdapter(int requestDelay, int responseDelay)
        {
            _requestDelay= requestDelay;
            _responseDelay= responseDelay;
            _semaphore = new SemaphoreSlim(1, 1);
        }

        public int RequestDelay { get => _requestDelay; set => Interlocked.Exchange(ref _requestDelay, value); }
        
        public int ResponseDelay { get => _responseDelay; set => Interlocked.Exchange(ref _responseDelay, value); }

        public async Task<Response> ReadAsync(CancellationToken cancellationToken)
        {
            if (_readCallsCounter > 0)
                throw new ConcurrentAccessException("Concurrent read detected");
            if (!_requestId.HasValue)
                throw new InvalidOperationException("Read called before Write");
            Interlocked.Increment(ref _readCallsCounter);
            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                await Task.Delay(_responseDelay, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _readCallsCounter);
                _semaphore.Release();
            }
            var response = new Response(_requestId!.Value);
            _requestId = null;
            return response;
        }

        public async Task WriteAsync(Request request, CancellationToken cancellationToken)
        {
            if (_writeCallsCounter > 0)
                throw new ConcurrentAccessException("Concurrent write detected");
            Interlocked.Increment(ref _writeCallsCounter);
            _requestId = request.Id;
            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                await Task.Delay(_requestDelay, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _writeCallsCounter);
                _semaphore.Release();
            }
        }
    }
}
