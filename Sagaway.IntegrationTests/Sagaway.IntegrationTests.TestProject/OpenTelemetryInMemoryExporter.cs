using System.Diagnostics;
using System.Text;
using OpenTelemetry;

namespace Sagaway.IntegrationTests.TestProject;

public class OpenTelemetryInMemoryExporter : BaseExporter<Activity>
{
    private readonly List<Activity> _activities = new();
    private readonly object _lock = new();
    private int _activityCounter = 0;

    public override ExportResult Export(in Batch<Activity> batch)
    {
        lock (_lock)
        {
            foreach (var activity in batch)
            {
                _activities.Add(activity);
            }
        }
        return ExportResult.Success;
    }

    public string GetSerializedActivities()
    {
        var builder = new StringBuilder();
        lock (_lock)
        {
            _activityCounter = 0; // Reset the counter for each serialization run
            foreach (var activity in _activities.OrderBy(a => a.StartTimeUtc))
            {
                SerializeActivityInternal(activity, builder, 0);
            }
            _activities.Clear(); // Optionally clear activities after serializing if they should only be reported once
        }
        return builder.ToString();
    }

    private void SerializeActivityInternal(Activity activity, StringBuilder builder, int indentLevel)
    {
        string indent = new string(' ', indentLevel * 2);
        builder.AppendLine($"{indent}Activity: {activity.DisplayName}");
        builder.AppendLine($"{indent}Sequence: {_activityCounter++}");
        builder.AppendLine($"{indent}Id: {activity.Id}");
        builder.AppendLine($"{indent}ParentId: {activity.ParentId}");

        if (activity.Tags.Any())
        {
            builder.AppendLine($"{indent}Tags:");
            foreach (var tag in activity.Tags)
            {
                builder.AppendLine($"{indent}  {tag.Key}={tag.Value}");
            }
        }

        if (activity.Events.Any())
        {
            builder.AppendLine($"{indent}Events:");
            foreach (var evt in activity.Events)
            {
                builder.AppendLine($"{indent}  Event: {evt.Name}, Sequence: {_activityCounter++}");
            }
        }
        builder.AppendLine($"{indent}---");
    }
}
