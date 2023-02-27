using Multiplexing.RequestProcessor.Tests.Mock;

namespace Multiplexing.RequestProcessor.Tests
{
    public class MockAdapterTests
    {
        [Fact]
        public async void WriteAsync_CancellationTokenExpired_ThrowsException()
        {
            var tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(100);
            var adapter = new FakeLowLevelNetworkAdapter(500, 0);
            try
            {
                await adapter.WriteAsync(new Request(Guid.Empty), tokenSource.Token);
            }
            catch (TaskCanceledException)
            {
                Assert.True(tokenSource.IsCancellationRequested);
                return;
            }
            Assert.Fail("Cancellation token timeout on WriteAsync not working");
        }

        [Fact]
        public async void ReadAsync_CancellationTokenExpired_ThrowsException()
        {
            var tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(100);
            var adapter = new FakeLowLevelNetworkAdapter(0, 500);
            try
            {
                await adapter.WriteAsync(new Request(Guid.Empty), CancellationToken.None);
                var result = await adapter.ReadAsync(tokenSource.Token);
            }
            catch (TaskCanceledException)
            {
                Assert.True(tokenSource.IsCancellationRequested);
                return;
            }
            Assert.Fail("Cancellation token timeout on ReadAsync not working");
        }

        [Fact]
        public async void ReadAsync_WhenWritingRequest_ReturnsRightResponse()
        {
            var requestId = Guid.Parse("26655BD5-A415-4A68-9D69-28113620A953");
            var adapter = new FakeLowLevelNetworkAdapter(10, 10);
            await adapter.WriteAsync(new Request(requestId), CancellationToken.None);
            var response = await adapter.ReadAsync(CancellationToken.None);
            Assert.Equal(requestId, response.Id);
        }

        [Fact]
        public async void WriteAsync_MultipleThreadCall_ThrowsException()
        {
            var adapter = new FakeLowLevelNetworkAdapter(100, 0);
            var task1 = adapter.WriteAsync(new Request(Guid.Parse("26655BD5-A415-4A68-9D69-28113620A953")), CancellationToken.None);
            var task2 = adapter.WriteAsync(new Request(Guid.Parse("09F080AF-6B86-4BC1-9BA3-77D45856EB49")), CancellationToken.None);
            try
            {
                await Task.WhenAll(task1, task2);
            }
            catch(Exception)
            {
                return;
            }
            Assert.Fail("WriteAsync works with 2 parallel threads");
        }

        [Fact]
        public async void ReadAsync_MultipleThreadCall_ThrowsException()
        {
            var adapter = new FakeLowLevelNetworkAdapter(0, 400);
            await adapter.WriteAsync(new Request(Guid.Empty), CancellationToken.None);
            var task1 = adapter.ReadAsync(CancellationToken.None);
            var task2 = adapter.ReadAsync(CancellationToken.None);
            try
            {
                await Task.WhenAll(task1, task2);
            }
            catch (Exception)
            {
                return;
            }
            Assert.Fail("ReadAsync works with 2 parallel threads");
        }
    }
}