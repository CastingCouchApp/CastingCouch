using System.IO.Compression;
using System.Text.Json;
using CreatorControlSuite.Modules.StreamDeck.Models;

namespace CreatorControlSuite.Modules.StreamDeck;

public sealed class StreamDeckProfileService
{
    private readonly string _outputRoot;

    public StreamDeckProfileService(string outputRoot)
    {
        _outputRoot = outputRoot;
        Directory.CreateDirectory(_outputRoot);
    }

    public async Task<StreamDeckProfilePackage> BuildDefaultProfileAsync(
        CancellationToken cancellationToken = default)
    {
        StreamDeckActionDefinition[] actions =
        [
            new StreamDeckActionDefinition(
                "prepare",
                "Vorbereiten",
                "workflow.prepare",
                "Startszene und Musik vorbereiten"),
            new StreamDeckActionDefinition(
                "countdown",
                "Countdown",
                "workflow.countdown",
                "Countdown starten"),
            new StreamDeckActionDefinition(
                "live",
                "Live",
                "workflow.live",
                "Auf Live wechseln"),
            new StreamDeckActionDefinition(
                "pause",
                "Pause",
                "workflow.pause",
                "Pausenszene aktivieren"),
            new StreamDeckActionDefinition(
                "resume",
                "Fortsetzen",
                "workflow.resume",
                "Live-Szene fortsetzen"),
            new StreamDeckActionDefinition(
                "end",
                "Beenden",
                "workflow.end",
                "Endszene und Streamende"),
            new StreamDeckActionDefinition(
                "stream-start",
                "Stream Start",
                "stream.start",
                "OBS-Stream direkt starten"),
            new StreamDeckActionDefinition(
                "stream-stop",
                "Stream Stop",
                "stream.stop",
                "OBS-Stream direkt beenden"),
            new StreamDeckActionDefinition(
                "spotify-toggle",
                "Spotify Play/Pause",
                "spotify.toggle",
                "Spotify Wiedergabe umschalten"),
            new StreamDeckActionDefinition(
                "spotify-play",
                "Spotify Play",
                "spotify.play",
                "Spotify Wiedergabe fortsetzen"),
            new StreamDeckActionDefinition(
                "spotify-pause",
                "Spotify Pause",
                "spotify.pause",
                "Spotify pausieren"),
            new StreamDeckActionDefinition(
                "spotify-next",
                "Spotify Weiter",
                "spotify.next",
                "Nächster Spotify-Titel"),
            new StreamDeckActionDefinition(
                "spotify-previous",
                "Spotify Zurück",
                "spotify.previous",
                "Vorheriger Spotify-Titel"),
            new StreamDeckActionDefinition(
                "spotify-volume-up",
                "Spotify lauter",
                "spotify.volumeup",
                "Spotify Lautstärke um 5 Prozent erhöhen"),
            new StreamDeckActionDefinition(
                "spotify-volume-down",
                "Spotify leiser",
                "spotify.volumedown",
                "Spotify Lautstärke um 5 Prozent verringern"),
            new StreamDeckActionDefinition(
                "spotify-volume-25",
                "Spotify 25%",
                "spotify.volume25",
                "Spotify Lautstärke auf 25 Prozent"),
            new StreamDeckActionDefinition(
                "spotify-volume-50",
                "Spotify 50%",
                "spotify.volume50",
                "Spotify Lautstärke auf 50 Prozent")
        ];

        DateTimeOffset timestamp = DateTimeOffset.Now;
        string directory = Path.Combine(
            _outputRoot,
            "CreatorControlSuite-Default");

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);

        var manifest = new
        {
            name = "Creator Control Suite",
            version = "6.5.0",
            commandClient = "CreatorControlSuite.CommandClient.exe",
            pipeName = "CreatorControlSuite.CommandPipe.v1",
            actions = actions.Select(action => new
            {
                id = action.Id,
                title = action.Title,
                command = action.Command,
                description = action.Description
            })
        };

        await File.WriteAllTextAsync(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }),
            cancellationToken);

        string packagePath = Path.Combine(
            _outputRoot,
            "CreatorControlSuite-Default.streamDeckProfile");

        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        ZipFile.CreateFromDirectory(
            directory,
            packagePath,
            CompressionLevel.Optimal,
            includeBaseDirectory: false);

        return new StreamDeckProfilePackage(
            "Creator Control Suite Standard",
            packagePath,
            timestamp,
            actions);
    }
}
