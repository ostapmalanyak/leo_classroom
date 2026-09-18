using System.Runtime.CompilerServices;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Provisioning;
using LeoClassroom.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class ProvisioningWorkerTests
{
    [Fact]
    public async Task StartupRecovery_ProcessesMoreRowsThanTheQueueCapacityWithoutEnqueueing()
    {
        var uow = Substitute.For<IUnitOfWork>();
        var repository = Substitute.For<IAcceptanceRepository>();
        uow.AcceptanceRepository.Returns(repository);
        IReadOnlyCollection<long> ids = Enumerable.Range(1, 1025).Select(id => (long) id).ToArray();
        repository.GetIdsByStatusAsync(SubmissionStatus.Provisioning)
                  .Returns(new ValueTask<IReadOnlyCollection<long>>(ids));
        var queue = Substitute.For<IProvisioningQueue>();
        queue.DequeueAllAsync(Arg.Any<CancellationToken>())
             .Returns(call => WaitForCancellationAsync(call.Arg<CancellationToken>()));

        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provisioning = Substitute.For<IRepoProvisioningService>();
        int processed = 0;
        provisioning.ProvisionAcceptanceAsync(Arg.Any<long>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref processed) == ids.Count)
            {
                recovered.TrySetResult();
            }
            OneOf<Success, NotFound, ProvisioningError> result = new Success();

            return ValueTask.FromResult(result);
        });
        await using ServiceProvider provider = new ServiceCollection()
            .AddSingleton(uow)
            .AddSingleton(provisioning)
            .BuildServiceProvider();
        using var worker = new ProvisioningWorker(queue, provider.GetRequiredService<IServiceScopeFactory>(),
                                                  Substitute.For<ILogger<ProvisioningWorker>>());
        var cancellationToken = TestContext.Current.CancellationToken;

        await worker.StartAsync(cancellationToken);
        try
        {
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            await queue.DidNotReceive().EnqueueAsync(Arg.Any<long>());
            processed.Should().Be(ids.Count);
        }
        finally
        {
            await worker.StopAsync(cancellationToken);
        }
    }

    private static async IAsyncEnumerable<long> WaitForCancellationAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }
}
