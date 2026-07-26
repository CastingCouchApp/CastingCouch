using System.Windows;
using CreatorControlSuite.Core.Ipc;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Core.Licensing;
using CreatorControlSuite.Modules.Alerts;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.Spotify;
using CreatorControlSuite.Modules.Workflow;

namespace CreatorControlSuite.App.Services;

public sealed class AppIpcCommandRouter : IIpcCommandRouter
{
    private readonly WorkflowModule _workflow;
    private readonly AlertsModule _alerts;
    private readonly IObsWebSocketClient _obs;
    private readonly SpotifyModule _spotify;
    private readonly IAppLogger _logger;
    private readonly IFeatureGate _featureGate;
    private readonly ExternalAlertActivityService _externalAlerts;

    public AppIpcCommandRouter(
        WorkflowModule workflow,
        AlertsModule alerts,
        IObsWebSocketClient obs,
        SpotifyModule spotify,
        IAppLogger logger,
        IFeatureGate featureGate,
        ExternalAlertActivityService externalAlerts)
    {
        _workflow=workflow; _alerts=alerts; _obs=obs; _spotify=spotify; _logger=logger; _featureGate=featureGate; _externalAlerts=externalAlerts;
    }

    public async Task<IpcResponse> ExecuteAsync(IpcCommand command, CancellationToken ct=default)
    {
        _logger.Write(AppLogLevel.Information,"IPC","Befehl: "+command.Command);
        switch(command.Command)
        {
            case "activate":
                var activationSucceeded = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Während Rechtszustimmung oder Ersteinrichtung darf eine zweite
                    // Instanz niemals das noch versteckte Hauptfenster einblenden.
                    // Bevorzugt wird deshalb das aktuell aktive bzw. sichtbare Fenster.
                    var window = Application.Current.Windows
                        .OfType<Window>()
                        .FirstOrDefault(candidate => candidate.IsActive)
                        ?? Application.Current.Windows
                            .OfType<Window>()
                            .FirstOrDefault(candidate => candidate.IsVisible)
                        ?? (Application.Current.MainWindow is { IsVisible: true }
                            ? Application.Current.MainWindow
                            : null);

                    if (window is null)
                        return false;

                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;

                    window.ShowInTaskbar = true;
                    window.Activate();
                    window.Topmost = true;
                    window.Topmost = false;
                    window.Focus();
                    return true;
                });

                return activationSucceeded
                    ? Ok(command,"Creator Control Suite wurde in den Vordergrund geholt.")
                    : new IpcResponse(
                        command.Id,
                        false,
                        "Die erste Instanz hat noch kein aktivierbares Fenster.",
                        new Dictionary<string,string>());
            case IpcCommandNames.Ping:
                return Ok(command,"pong");
            case IpcCommandNames.Status:
                var state=_workflow.Service.State;
                return new(command.Id,true,"Status",new Dictionary<string,string>
                {
                    ["phase"]=state.Phase.ToString(),
                    ["scene"]=state.CurrentScene,
                    ["detail"]=state.Detail
                });
            case IpcCommandNames.Prepare:
                await _featureGate.RequireAsync(FeatureCatalog.Workflow, ct);
                await _workflow.Service.PrepareAsync(ct); return Ok(command,"Vorbereitet.");
            case IpcCommandNames.Countdown:
                _=Task.Run(()=>_workflow.Service.StartCountdownAsync()); return Ok(command,"Countdown gestartet.");
            case IpcCommandNames.Live:
                await _workflow.Service.GoLiveAsync(ct); return Ok(command,"Live.");
            case IpcCommandNames.Pause:
                await _workflow.Service.PauseAsync(ct); return Ok(command,"Pause.");
            case IpcCommandNames.Resume:
                await _workflow.Service.ResumeAsync(ct); return Ok(command,"Fortgesetzt.");
            case IpcCommandNames.End:
                _=Task.Run(()=>_workflow.Service.EndAsync()); return Ok(command,"Streamende gestartet.");
            case IpcCommandNames.AlertTest:
                await _featureGate.RequireAsync(FeatureCatalog.Alerts, ct);
                await _alerts.EnqueueAsync(
                    Get(command,"type","Follow"),
                    Get(command,"user","StreamDeck"),
                    cancellationToken:ct);
                return Ok(command,"Testalert eingereiht.");
            case IpcCommandNames.ExternalAlertStart:
                _externalAlerts.Start(Get(command,"source","streamerbot"), Get(command,"id","default"));
                return Ok(command,"Externer Alert gestartet.");
            case IpcCommandNames.ExternalAlertEnd:
                _externalAlerts.End(Get(command,"source","streamerbot"), Get(command,"id","default"));
                return Ok(command,"Externer Alert beendet.");
            case IpcCommandNames.ExternalAlertClear:
                _externalAlerts.ClearSource(Get(command,"source","streamerbot"));
                return Ok(command,"Externe Alerts zurückgesetzt.");
            case IpcCommandNames.ObsScene:
                await _featureGate.RequireAsync(FeatureCatalog.Obs, ct);
                var scene=Get(command,"scene","");
                if(string.IsNullOrWhiteSpace(scene)) throw new InvalidOperationException("Argument scene fehlt.");
                await _obs.SetCurrentProgramSceneAsync(scene,ct);
                return Ok(command,"Szene aktiviert: "+scene);
            case IpcCommandNames.ObsMute:
                await _featureGate.RequireAsync(FeatureCatalog.Obs, ct);
                var input=Get(command,"input","");
                if(string.IsNullOrWhiteSpace(input)) throw new InvalidOperationException("Argument input fehlt.");
                var muted=bool.TryParse(Get(command,"muted","true"),out var muteValue) && muteValue;
                await _obs.SetInputMuteAsync(input,muted,ct);
                return Ok(command,$"{input}: {(muted ? "stumm" : "aktiv")}.");
            case IpcCommandNames.SpotifyPlay:
                await _spotify.ResumeAsync(ct);
                return Ok(command,"Spotify Wiedergabe fortgesetzt.");
            case IpcCommandNames.SpotifyPause:
                await _spotify.PauseAsync(ct);
                return Ok(command,"Spotify pausiert.");
            case IpcCommandNames.SpotifyNext:
                await _spotify.NextAsync(ct);
                return Ok(command,"Nächster Spotify-Titel.");
            case IpcCommandNames.SpotifyPrevious:
                await _spotify.PreviousAsync(ct);
                return Ok(command,"Vorheriger Spotify-Titel.");
            case IpcCommandNames.SpotifyVolume:
                if(!int.TryParse(Get(command,"volume",""),out var volume))
                    throw new InvalidOperationException("Argument volume fehlt oder ist ungültig.");
                await _spotify.SetVolumeAsync(Math.Clamp(volume,0,100),ct);
                return Ok(command,$"Spotify Lautstärke: {Math.Clamp(volume,0,100)} %.");
            case IpcCommandNames.SpotifyVolume25:
                await _spotify.SetVolumeAsync(25,ct);
                return Ok(command,"Spotify Lautstärke: 25 %.");
            case IpcCommandNames.SpotifyVolume50:
                await _spotify.SetVolumeAsync(50,ct);
                return Ok(command,"Spotify Lautstärke: 50 %.");
            case IpcCommandNames.StreamStart:
                await _featureGate.RequireAsync(FeatureCatalog.Obs, ct);
                await _obs.StartStreamAsync(ct);
                return Ok(command,"OBS-Stream gestartet.");
            case IpcCommandNames.StreamStop:
                await _featureGate.RequireAsync(FeatureCatalog.Obs, ct);
                await _obs.StopStreamAsync(ct);
                return Ok(command,"OBS-Stream beendet.");
            default:
                return new(command.Id,false,"Unbekannter Befehl.",new Dictionary<string,string>());
        }
    }

    private static IpcResponse Ok(IpcCommand c,string m)=>
        new(c.Id,true,m,new Dictionary<string,string>());

    private static string Get(IpcCommand c,string key,string fallback)=>
        c.Arguments.TryGetValue(key,out var value)?value:fallback;
}
