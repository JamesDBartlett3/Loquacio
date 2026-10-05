using Xunit;

namespace Loquacio.Tests.Infrastructure;

/// <summary>
/// The audio channel is process-global static, so any test that reads or
/// writes it (or starts a real BackgroundTranscriptionService that drains it)
/// must run sequentially with respect to the others — parallel execution lets
/// one test steal another's segments and hang its event waits.
/// </summary>
[CollectionDefinition("StaticAudioChannel")]
public sealed class StaticAudioChannelCollection;
