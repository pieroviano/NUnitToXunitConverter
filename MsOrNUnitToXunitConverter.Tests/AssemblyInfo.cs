using Xunit;

// The converter logs through the process-wide LoggerFactoryContainer, and several tests install their own
// factory to capture what it writes. With classes running in parallel that shared state races: a test sees
// another class's log lines in its sink, or has its own dropped because the container momentarily held a
// factory with a higher minimum level. The whole suite runs in well under a second, so serialising it costs
// nothing and removes the entire class of flakiness.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
