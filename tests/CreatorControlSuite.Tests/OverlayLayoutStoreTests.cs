using System.Text.Json;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Overlay.Models;

namespace CreatorControlSuite.Tests;

public sealed class OverlayLayoutStoreTests
{
    [Fact]
    public void OverlayLayout_SerializesCamelCase()
    {
        var layout = new OverlayLayout
        {
            Version = 1,
            CanvasWidth = 1920,
            CanvasHeight = 1080,
            Items =
            [
                new OverlayLayoutItem
                {
                    Id = "a1",
                    Kind = "widget",
                    Type = "spotify",
                    X = 10,
                    Y = 20,
                    W = 950,
                    H = 188,
                    Z = 1,
                    Props = new Dictionary<string, JsonElement>
                    {
                        ["showProgress"] = JsonSerializer.SerializeToElement(true)
                    }
                }
            ]
        };

        string json = JsonSerializer.Serialize(layout, OverlayLayoutStore.JsonOptions);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(1920, doc.RootElement.GetProperty("canvasWidth").GetInt32());
        Assert.Equal("spotify", doc.RootElement.GetProperty("items")[0].GetProperty("type").GetString());
        Assert.True(doc.RootElement.GetProperty("items")[0].GetProperty("props").GetProperty("showProgress").GetBoolean());
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsLayout()
    {
        string root = Path.Combine(Path.GetTempPath(), "ccs-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OverlayLayoutStore(root);
            var layout = OverlayLayout.CreateDefault();
            layout.Items.Add(new OverlayLayoutItem
            {
                Id = "online1",
                Kind = "widget",
                Type = "online",
                X = 100,
                Y = 40,
                W = 280,
                H = 80,
                Z = 2
            });

            await store.SaveAsync("inst1", layout);

            OverlayLayout loaded = await store.LoadAsync("inst1");
            Assert.Equal(1920, loaded.CanvasWidth);
            Assert.Single(loaded.Items);
            Assert.Equal("online", loaded.Items[0].Type);
            Assert.Equal(100, loaded.Items[0].X);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsDefaultEmptyLayout()
    {
        string root = Path.Combine(Path.GetTempPath(), "ccs-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OverlayLayoutStore(root);
            OverlayLayout layout = await store.LoadAsync("missing");
            Assert.Equal(1, layout.Version);
            Assert.Equal(1920, layout.CanvasWidth);
            Assert.Empty(layout.Items);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Save_RejectsInvalidInstanceId()
    {
        string root = Path.Combine(Path.GetTempPath(), "ccs-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OverlayLayoutStore(root);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.SaveAsync("../evil", OverlayLayout.CreateDefault()));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Save_AtomicWrite_SurvivesReload()
    {
        string root = Path.Combine(Path.GetTempPath(), "ccs-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OverlayLayoutStore(root);
            for (int i = 0; i < 5; i++)
            {
                var layout = OverlayLayout.CreateDefault();
                layout.Items.Add(new OverlayLayoutItem
                {
                    Id = "n" + i,
                    Kind = "widget",
                    Type = "alert",
                    X = i,
                    Y = i,
                    W = 400,
                    H = 120,
                    Z = i
                });
                await store.SaveAsync("round", layout);
            }

            OverlayLayout loaded = await store.LoadAsync("round");
            Assert.Equal("n4", loaded.Items[0].Id);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsName()
    {
        string root = Path.Combine(Path.GetTempPath(), "ccs-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OverlayLayoutStore(root);
            var layout = OverlayLayout.CreateDefault();
            layout.Name = "Just Chatting";
            await store.SaveAsync("chat", layout);

            OverlayLayout loaded = await store.LoadAsync("chat");
            Assert.Equal("Just Chatting", loaded.Name);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ListExistsDeleteDuplicate_Work()
    {
        string root = Path.Combine(Path.GetTempPath(), "ccs-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OverlayLayoutStore(root);
            var layout = OverlayLayout.CreateDefault();
            layout.Name = "Source";
            layout.Items.Add(new OverlayLayoutItem
            {
                Id = "w1",
                Kind = "widget",
                Type = "music",
                X = 5,
                Y = 10,
                W = 300,
                H = 100,
                Z = 1
            });
            await store.SaveAsync("source", layout);

            Assert.True(store.Exists("source"));
            Assert.False(store.Exists("copy"));
            Assert.Equal(["source"], store.ListInstanceIds());

            await store.DuplicateAsync("source", "copy");
            Assert.True(store.Exists("copy"));
            OverlayLayout copy = await store.LoadAsync("copy");
            Assert.Single(copy.Items);
            Assert.Equal("music", copy.Items[0].Type);
            Assert.Equal(["copy", "source"], store.ListInstanceIds().OrderBy(x => x, StringComparer.Ordinal).ToArray());

            await store.DeleteAsync("source");
            Assert.False(store.Exists("source"));
            Assert.Equal(["copy"], store.ListInstanceIds());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
