using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lib.Build;

internal interface IMDictDataBuilder
{
    public MDictData BuildData(List<MDictEntry> entries, MDictMetadata metadata);
}

internal static class MDictDataBuilderProvider
{
    public static IMDictDataBuilder GetDataBuilder(bool logging)
        => new ServiceCollection()

        // Offset table
        .AddTransient<OffsetTableBuilder>()
        .AddTransient<MDictKeyComparer>()

        // Key blocks
        .AddTransient<KeyBlockIndexBuilder>()
        .AddTransient<KeyBlocksBuilder>()

        // Record blocks
        .AddTransient<RecordBlockIndexBuilder>()
        .AddTransient<RecordBlocksBuilder>()

        // Logging
        .AddLogging(builder =>
        {
            if (logging) builder.SetMinimumLevel(LogLevel.Debug);

            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = false;
                options.TimestampFormat = "HH:mm:ss ";
            });
        })

        // Build and return the builder service.
        .AddTransient<IMDictDataBuilder, MDictDataBuilder>()
        .BuildServiceProvider()
        .GetRequiredService<IMDictDataBuilder>();
}
