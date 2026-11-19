using OsSim.Domain.Simulation;

namespace OsSim.Domain.ResourceAllocation;

public interface IBankerModule : ISimulationModule<BankerInput, BankerSnapshot>
{
    BankerRequestResult TestRequest(BankerInput currentState, BankerResourceRequest request);
}
