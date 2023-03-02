using Multiplexing.RequestProcessor.Tests.Mock;
using System.Diagnostics;

namespace Multiplexing.RequestProcessor.Tests
{
    public class ComplexRequestProcessorTests
    {
        [Fact]
        public async void SendAsync_OneTaskProcessing_ReturnsCorrectResponse()
        {
            var adapter = new FakeLowLevelNetworkAdapter(100, 100);
            var processor = new ComplexRequestProcessor(adapter, TimeSpan.FromSeconds(1));
            await processor.StartAsync(CancellationToken.None);
            var request = new Request(Guid.NewGuid());
            var res = await processor.SendAsync(request, CancellationToken.None);
            await processor.StopAsync(CancellationToken.None);
            Assert.Equal(request.Id, res.Id);
        }

        [Theory]
        [InlineData(10, 100, 10)]
        [InlineData(10, 10, 100)]
        public async void SendAsync_MultipleTaskProcessing_ReturnsCorrectReponses(int taskCount, int writeDelay, int readDelay)
        {
            var adapter = new FakeLowLevelNetworkAdapter(writeDelay, readDelay);
            var processor = new ComplexRequestProcessor(adapter, TimeSpan.FromSeconds(5));
            await processor.StartAsync(CancellationToken.None);
            var tasks = new Dictionary<Guid, Task<Response>>();
            for (var i = 0; i < taskCount; i++)
            {
                var request = new Request(Guid.NewGuid());
                Debug.WriteLine($"Request {i + 1} with id {request.Id} started");
                try
                {
                    var task = processor.SendAsync(request, CancellationToken.None);
                    tasks.Add(request.Id, task);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                }

            }
            await Task.WhenAll(tasks.Values.ToArray());
            await processor.StopAsync(CancellationToken.None);
            foreach (var pair in tasks)
            {
                Assert.Equal(pair.Key, pair.Value.Result.Id);
            }
        }

        [Fact]
        public async void SendAsync_OneTaskProcessing_CancellationWorks()
        {
            var adapter = new FakeLowLevelNetworkAdapter(500, 500);
            var processor = new ComplexRequestProcessor(adapter, TimeSpan.FromSeconds(2));
            var cts = new CancellationTokenSource();
            await processor.StartAsync(CancellationToken.None);
            var request = new Request(Guid.NewGuid());
            try
            {
                var resTask = processor.SendAsync(request, cts.Token);
                await Task.Delay(100);
                cts.Cancel();
                var res = await resTask;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await processor.StopAsync(CancellationToken.None);
            Assert.Fail("Task wasn't cancelled");
        }

        [Fact]
        public async void StopAsync_MethodCall_CancellationWorks()
        {
            var adapter = new FakeLowLevelNetworkAdapter(100, 100);
            var processor = new ComplexRequestProcessor(adapter, TimeSpan.FromSeconds(1));
            await processor.StartAsync(CancellationToken.None);
            var tasks = new Dictionary<Guid, Task<Response>>();
            for (var i = 0; i < 5; i++)
            {

                var request = new Request(Guid.NewGuid());
                var task = processor.SendAsync(request, CancellationToken.None);
                tasks.Add(request.Id, task);
            }
            var cts = new CancellationTokenSource();
            cts.Cancel();
            try
            {
                await processor.StopAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            Assert.Fail("Task wasn't cancelled");
        }

        [Fact]
        public async void SendAsync_OneRequestProcessing_TimeoutsCorrectrly()
        {
            var adapter = new FakeLowLevelNetworkAdapter(1000, 1000);
            var processor = new ComplexRequestProcessor(adapter, TimeSpan.FromMilliseconds(100));
            await processor.StartAsync(CancellationToken.None);
            var request = new Request(Guid.NewGuid());
            try
            {
                var res = await processor.SendAsync(request, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await processor.StopAsync(CancellationToken.None);
            Assert.Fail("Timeout works wrong");
        }

        private async Task CycleTimeoutTest(FakeLowLevelNetworkAdapter adapter,
                                     IRequestProcessor processor,
                                     int timeout,
                                     int delayMultiplier,
                                     int cycles,
                                     bool increaseReqTimeout,
                                     bool increaseRespTimeout)
        {
            for (var i = 1; i < cycles + 1; i++)
            {
                var request = new Request(Guid.NewGuid());
                var delay = delayMultiplier * i;
                try
                {
                    adapter.RequestDelay = increaseReqTimeout ? delay : 0;
                    adapter.ResponseDelay = increaseRespTimeout ? delay : 0;
                    var response = await processor.SendAsync(request, CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                    if (delay < timeout)
                    {
                        Assert.Fail("Operation cancelled unexpectedly");
                    }
                    continue;
                }
                if (delay >= timeout)
                {
                    Assert.Fail("Operation not cancelled by timeout");
                }
            }
        }

        [Theory]
        [InlineData(500, 200, 5, true, false)]
        [InlineData(500, 200, 5, false, true)]
        public async Task SendAsync_MultipleRequestProcessing_RequestTimeoutsCorrectrly(int timeout,
                                                                                        int delayMultiplier,
                                                                                        int cycles,
                                                                                        bool increaseReqTimeout,
                                                                                        bool increaseRespTimeout)
        {
            var adapter = new FakeLowLevelNetworkAdapter(0, 0);
            var processor = new ComplexRequestProcessor(adapter, TimeSpan.FromMilliseconds(timeout));
            await processor.StartAsync(CancellationToken.None);
            await CycleTimeoutTest(adapter, processor, timeout, delayMultiplier, cycles, increaseReqTimeout, increaseRespTimeout);
            await processor.StopAsync(CancellationToken.None);
        }
    }
}
