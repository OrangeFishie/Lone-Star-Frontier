using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Audio.Jukebox;
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using JukeboxComponent = Content.Shared.Audio.Jukebox.JukeboxComponent;

namespace Content.Server.Audio.Jukebox;


public sealed class JukeboxSystem : SharedJukeboxSystem
{
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly AppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JukeboxComponent, JukeboxSelectedMessage>(OnJukeboxSelected);
        SubscribeLocalEvent<JukeboxComponent, JukeboxPlayingMessage>(OnJukeboxPlay);
        SubscribeLocalEvent<JukeboxComponent, JukeboxPauseMessage>(OnJukeboxPause);
        SubscribeLocalEvent<JukeboxComponent, JukeboxStopMessage>(OnJukeboxStop);
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetTimeMessage>(OnJukeboxSetTime);
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetVolumeMessage>(OnJukeboxSetVolume); /// ADT-Tweak
        SubscribeLocalEvent<JukeboxComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<JukeboxComponent, ComponentShutdown>(OnComponentShutdown);

        SubscribeLocalEvent<JukeboxComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<JukeboxComponent, JukeboxToggleLoopMessage>(OnJukeboxToggleLoop);

        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);
    }

    private void OnComponentInit(EntityUid uid, JukeboxComponent component, ComponentInit args)
    {
        if (HasComp<ApcPowerReceiverComponent>(uid))
        {
            TryUpdateVisualState(uid, component);
        }
    }

    private void OnJukeboxPlay(EntityUid uid, JukeboxComponent component, ref JukeboxPlayingMessage args)
    {
        if (_entManager.TryGetComponent(component.AudioStream, out AudioComponent? audioComp) &&
            audioComp.Playing)
        {
            Audio.SetState(component.AudioStream, AudioState.Playing);
        }
        else
        {
            component.AudioStream = Audio.Stop(component.AudioStream);

            if (string.IsNullOrEmpty(component.SelectedSongId) ||
                !_protoManager.TryIndex(component.SelectedSongId, out var jukeboxProto))
            {
                return;
            }
            var audioParams = new AudioParams
            {
                MaxDistance = 10f,
                Loop = component.Loop,
                Volume = 1f
            };

            component.AudioStream = Audio.PlayPvs(jukeboxProto.Path, uid, AudioParams.Default.WithMaxDistance(10f))?.Entity;
            Dirty(uid, component);
        }
    }

    private void OnJukeboxToggleLoop(Entity<JukeboxComponent> ent, ref JukeboxToggleLoopMessage args)
    {
        ent.Comp.Loop = !ent.Comp.Loop;
        Dirty(ent);

        if (_entManager.TryGetComponent(ent.Comp.AudioStream, out AudioComponent? audioComp) &&
            audioComp.Playing)
        {
            var position = audioComp.PlaybackPosition;
            var songId = ent.Comp.SelectedSongId;

            Audio.Stop(ent.Comp.AudioStream);

            if (_protoManager.TryIndex(songId, out var jukeboxProto))
            {
                var audioParams = new AudioParams
                {
                    MaxDistance = 10f,
                    Loop = ent.Comp.Loop,
                    Volume = audioComp.Volume
                };

                ent.Comp.AudioStream = Audio.PlayPvs(jukeboxProto.Path, ent, audioParams)?.Entity;

                if (ent.Comp.AudioStream != null)
                {
                    Audio.SetPlaybackPosition(ent.Comp.AudioStream, position);
                }

                Dirty(ent);
            }
        }
    }

    private void OnJukeboxPause(Entity<JukeboxComponent> ent, ref JukeboxPauseMessage args)
    {
        if (ent.Comp.AudioStream != null && Exists(ent.Comp.AudioStream.Value) && HasComp<MetaDataComponent>(ent.Comp.AudioStream.Value))
        {
            Audio.SetState(ent.Comp.AudioStream, AudioState.Paused);
        }
    }

    private void OnJukeboxSetTime(EntityUid uid, JukeboxComponent component, JukeboxSetTimeMessage args)
    {
        if (TryComp(args.Actor, out ActorComponent? actorComp))
        {
            var offset = actorComp.PlayerSession.Channel.Ping * 1.5f / 1000f;

            if (component.AudioStream != null && Exists(component.AudioStream.Value) && HasComp<MetaDataComponent>(component.AudioStream.Value))
            {
                Audio.SetPlaybackPosition(component.AudioStream, args.SongTime + offset);
            }
        }
    }

    /// ADT-Tweak start
    private void OnJukeboxSetVolume(EntityUid uid, JukeboxComponent component, JukeboxSetVolumeMessage args)
    {
        SetJukeboxVolume(uid, component, args.Volume);

        if (!TryComp<AudioComponent>(component.AudioStream, out var audioComponent))
            return;

        Audio.SetVolume(component.AudioStream, MapToRange(args.Volume, component.MinSlider, component.MaxSlider, component.MinVolume, component.MaxVolume));
    }
    /// ADT-Tweak end

    private void OnPowerChanged(Entity<JukeboxComponent> entity, ref PowerChangedEvent args)
    {
        TryUpdateVisualState(entity);

        if (!this.IsPowered(entity.Owner, EntityManager))
        {
            Stop(entity);
        }
    }

    private void OnJukeboxStop(Entity<JukeboxComponent> entity, ref JukeboxStopMessage args)
    {
        Stop(entity);
    }

    private void Stop(Entity<JukeboxComponent> entity)
    {
        if (entity.Comp.AudioStream != null && Exists(entity.Comp.AudioStream.Value) && HasComp<MetaDataComponent>(entity.Comp.AudioStream.Value))
        {
            Audio.SetState(entity.Comp.AudioStream, AudioState.Stopped);
        }

        Dirty(entity);
    }

    private void OnJukeboxSelected(EntityUid uid, JukeboxComponent component, JukeboxSelectedMessage args)
    {
        component.SelectedSongId = args.SongId;
        DirectSetVisualState(uid, JukeboxVisualState.Select);
        component.Selecting = true;
        component.AudioStream = Audio.Stop(component.AudioStream);

        // Автоматически запускаем новую песню
        if (_protoManager.TryIndex(args.SongId, out var jukeboxProto))
        {
            var audioParams = new AudioParams
            {
                MaxDistance = 10f,
                Loop = component.Loop,
                Volume = SharedJukeboxSystem.MapToRange(component.Volume, component.MinSlider, component.MaxSlider, component.MinVolume, component.MaxVolume)
            };

            component.AudioStream = Audio.PlayPvs(jukeboxProto.Path, uid, audioParams)?.Entity;
            Dirty(uid, component);
        }

        Dirty(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<JukeboxComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Selecting)
            {
                comp.SelectAccumulator += frameTime;
                if (comp.SelectAccumulator >= 0.5f)
                {
                    comp.SelectAccumulator = 0f;
                    comp.Selecting = false;

                    TryUpdateVisualState(uid, comp);
                }
            }
        }
    }

    /// ADT-Tweak start
    private void SetJukeboxVolume(EntityUid uid, JukeboxComponent component, float volume)
    {
        component.Volume = volume;
        Dirty(uid, component);
    }
    /// ADT-Tweak end

    private void OnComponentShutdown(EntityUid uid, JukeboxComponent component, ComponentShutdown args)
    {
        component.AudioStream = Audio.Stop(component.AudioStream);
    }

    /// <summary>
    /// Handles entity deletion to clean up AudioStream references in JukeboxComponents.
    /// </summary>
    private void OnEntityTerminating(ref EntityTerminatingEvent args)
    {
        var terminatingEntity = args.Entity.Owner;

        // Check all jukebox components to see if any reference the terminating entity
        var query = EntityQueryEnumerator<JukeboxComponent>();
        while (query.MoveNext(out var jukeboxUid, out var jukeboxComp))
        {
            // If this jukebox's AudioStream references the terminating entity, clear it
            if (jukeboxComp.AudioStream == terminatingEntity)
            {
                jukeboxComp.AudioStream = null;
                Dirty(jukeboxUid, jukeboxComp);
            }
        }
    }

    private void DirectSetVisualState(EntityUid uid, JukeboxVisualState state)
    {
        _appearanceSystem.SetData(uid, JukeboxVisuals.VisualState, state);
    }

    private void TryUpdateVisualState(EntityUid uid, JukeboxComponent? jukeboxComponent = null)
    {
        if (!Resolve(uid, ref jukeboxComponent))
            return;

        var finalState = JukeboxVisualState.On;

        if (!this.IsPowered(uid, EntityManager))
        {
            finalState = JukeboxVisualState.Off;
        }

        _appearanceSystem.SetData(uid, JukeboxVisuals.VisualState, finalState);
    }
}
