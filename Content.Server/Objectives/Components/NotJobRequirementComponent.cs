using Content.Server.Objectives.Systems;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;

/// <summary>
/// Requires that the player not have certain job(s) to have this objective.
/// </summary>
[RegisterComponent, Access(typeof(NotJobRequirementSystem))]
public sealed partial class NotJobRequirementComponent : Component
{
    /// <summary>
    /// ID of the job to ban from having this objective.
    /// Can be used for a single job, or use Jobs for multiple jobs.
    /// </summary>
    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<JobPrototype>))]
    public string Job = string.Empty;

    /// <summary>
    /// IDs of the jobs to ban from having this objective.
    /// </summary>
    [DataField(customTypeSerializer: typeof(PrototypeIdListSerializer<JobPrototype>))]
    public List<string> Jobs = new();
}
