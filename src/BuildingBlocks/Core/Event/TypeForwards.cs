using System.Runtime.CompilerServices;
using BuildingBlocks.Core.Event;

// IEvent and IIntegrationEvent moved to the Contracts.Messages package (same namespace); forward them so binaries
// compiled against an older BuildingBlocks.Core keep resolving the types.
[assembly: TypeForwardedTo(typeof(IEvent))]
[assembly: TypeForwardedTo(typeof(IIntegrationEvent))]
