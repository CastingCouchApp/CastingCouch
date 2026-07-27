using CreatorControlSuite.Modules.Overlay;

namespace CreatorControlSuite.Tests;

public sealed class CanvasOverlayAssetsTests
{
    [Theory]
    [InlineData("shared/runtime.js")]
    [InlineData("shared/styles.css")]
    [InlineData("editor/index.html")]
    [InlineData("editor/editor.js")]
    [InlineData("view/index.html")]
    [InlineData("solo/index.html")]
    public void TryGet_EmbeddedCanvasAssets(string path)
    {
        Assert.True(CanvasOverlayAssets.TryGet(path, out string content, out string contentType));
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.False(string.IsNullOrWhiteSpace(contentType));
    }

    [Fact]
    public void ListWidgetTypes_ContainsOnlineAlertMusicChatEndingStatsTextImageCountdownSocials()
    {
        IReadOnlyList<string> types = CanvasOverlayAssets.ListWidgetTypes();
        Assert.Contains("online", types);
        Assert.Contains("alert", types);
        Assert.Contains("music", types);
        Assert.Contains("chat", types);
        Assert.Contains("ending-stats", types);
        Assert.Contains("text", types);
        Assert.Contains("image", types);
        Assert.Contains("countdown", types);
        Assert.Contains("socials", types);
        Assert.DoesNotContain("spotify", types);
    }

    [Fact]
    public void CanvasRuntime_RegistersMusicWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("music:", runtime);
        Assert.Contains("createSpotifyEl", runtime);
        Assert.Contains("updateSpotify", runtime);
        Assert.Contains("paintSpotifyProgress", runtime);
        Assert.Contains("MUSIC_VARIANTS", runtime);
        Assert.Contains("MUSIC_SIZE_PRESETS", runtime);
        Assert.Contains("applyMusicVariant", runtime);
        Assert.Contains("syncMusicMarquee", runtime);
        Assert.Contains("sizePreset", runtime);
        Assert.Contains("ccs-music-marquee", runtime);
        Assert.Contains("ResizeObserver", runtime);
        Assert.Contains("\"classic\"", runtime);
        Assert.Contains("\"neon\"", runtime);
        Assert.Contains("\"vinyl\"", runtime);
        Assert.Contains("\"hud\"", runtime);
        Assert.Contains("\"aurora\"", runtime);
        Assert.Contains("\"ticker\"", runtime);
        Assert.Contains("\"bubble\"", runtime);
        Assert.Contains("\"ember\"", runtime);
        Assert.Contains("Vinyl", runtime);
        Assert.Contains("Banner", runtime);
        Assert.Contains("Classic", runtime);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(runtime, "\"(?:classic|neon|minimal|glass|bold|outline|cyber|soft|solid|gradient|vinyl|hud|pill|stacked|ticker|aurora|mono|retro|bubble|stripe|frost|ember)\"").Count >= 20);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"music\"", editor);
        Assert.Contains("selectProp(\"variant\"", editor);
        Assert.Contains("selectProp(\"sizePreset\"", editor);
        Assert.Contains("MUSIC_VARIANTS", editor);
        Assert.Contains("MUSIC_SIZE_PRESETS", editor);
        Assert.Contains("MUSIC_VARIANT_LABELS", editor);
        Assert.Contains("music-look", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-spotify", styles);
        Assert.Contains(".ccs-music-marquee", styles);
        Assert.Contains("ccs-music-marquee-scroll", styles);
        Assert.Contains("ccs-spotify-v-classic", styles);
        Assert.Contains("ccs-spotify-v-neon", styles);
        Assert.Contains("ccs-spotify-v-vinyl", styles);
        Assert.Contains("ccs-spotify-v-hud", styles);
        Assert.Contains("ccs-spotify-v-aurora", styles);
        Assert.Contains("ccs-spotify-v-ticker", styles);
        Assert.Contains("ccs-spotify-v-bubble", styles);
        Assert.Contains("ccs-spotify-v-ember", styles);
        Assert.Contains("--ccs-music-scale", styles);
        Assert.Contains("container-type", styles);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(styles, @"ccs-spotify-v-(?:classic|neon|minimal|glass|bold|outline|cyber|soft|solid|gradient|vinyl|hud|pill|stacked|ticker|aurora|mono|retro|bubble|stripe|frost|ember)").Count >= 20);
    }

    [Fact]
    public void CanvasRuntime_RegistersTextWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("text:", runtime);
        Assert.Contains("createTextEl", runtime);
        Assert.Contains("updateText", runtime);
        Assert.Contains("content:", runtime);
        Assert.Contains("fontSizePx", runtime);
        Assert.Contains("letterSpacingPx", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"text\"", editor);
        Assert.Contains("textProp(\"content\"", editor);
        Assert.Contains("selectProp(\"align\"", editor);
        Assert.Contains("fontSizePx", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-text", styles);
        Assert.Contains("--ccs-text-size", styles);
    }

    [Fact]
    public void CanvasRuntime_RegistersImageWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("image:", runtime);
        Assert.Contains("createImageEl", runtime);
        Assert.Contains("updateImage", runtime);
        Assert.Contains("objectFit", runtime);
        Assert.Contains("borderRadiusPx", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"image\"", editor);
        Assert.Contains("textProp(\"src\"", editor);
        Assert.Contains("selectProp(\"fit\"", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-image", styles);
        Assert.Contains(".ccs-image-media", styles);
    }

    [Fact]
    public void CanvasRuntime_RegistersCountdownWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("countdown:", runtime);
        Assert.Contains("createCountdownEl", runtime);
        Assert.Contains("updateCountdown", runtime);
        Assert.Contains("paintCountdown", runtime);
        Assert.Contains("data.countdown", runtime);
        Assert.Contains("remainingSeconds", runtime);
        Assert.Contains("endsAt", runtime);
        Assert.Contains("app.countdown", runtime);
        Assert.Contains("hideWhenIdle", runtime);
        Assert.Contains("ResizeObserver", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"countdown\"", editor);
        Assert.Contains("selectProp(\"variant\"", editor);
        Assert.Contains("selectProp(\"format\"", editor);
        Assert.Contains("boolProp(\"showLabel\"", editor);
        Assert.Contains("boolProp(\"hideWhenIdle\"", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-countdown", styles);
        Assert.Contains("ccs-countdown-v-classic", styles);
        Assert.Contains("ccs-countdown-v-neon", styles);
        Assert.Contains("ccs-countdown-v-minimal", styles);
        Assert.Contains("ccs-countdown-v-bold", styles);
        Assert.Contains("--ccs-countdown-scale", styles);
    }

    [Fact]
    public void CanvasRuntime_RegistersChatWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("chat:", runtime);
        Assert.Contains("createChatEl", runtime);
        Assert.Contains("channel.chat.message", runtime);
        Assert.Contains("chatHistory", runtime);
        Assert.Contains("/chat/history", runtime);
        Assert.Contains("messageId", runtime);
        Assert.Contains("fontSizePx", runtime);
        Assert.Contains("fontFamily", runtime);
        Assert.Contains("backgroundType", runtime);
        Assert.Contains("resolveChatAppearance", runtime);
        Assert.DoesNotContain("updateChatProps", runtime);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(runtime, "function updateChat\\b"));

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"chat\"", editor);
        Assert.Contains("fontSizePx", editor);
        Assert.Contains("fontFamily", editor);
        Assert.Contains("backgroundType", editor);
        Assert.Contains("paddingPx", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-chat", styles);
        Assert.Contains("--ccs-chat-font-size", styles);
    }

    [Fact]
    public void CanvasRuntime_RegistersEndingStatsWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"ending-stats\":", runtime);
        Assert.Contains("createEndingStatsEl", runtime);
        Assert.Contains("updateEndingStats", runtime);
        Assert.Contains("fitEndingStats", runtime);
        Assert.Contains("variant:", runtime);
        Assert.Contains("followersGained", runtime);
        Assert.Contains("peakViewers", runtime);
        Assert.Contains("averageViewers", runtime);
        Assert.Contains("ResizeObserver", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"ending-stats\"", editor);
        Assert.Contains("selectProp(\"variant\"", editor);
        Assert.Contains("Classic", editor);
        Assert.Contains("Neon", editor);
        Assert.Contains("Compact", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-ending-stats", styles);
        Assert.Contains("ccs-ending-stats-v-classic", styles);
        Assert.Contains("ccs-ending-stats-v-neon", styles);
        Assert.Contains("ccs-ending-stats-v-minimal", styles);
        Assert.Contains("ccs-ending-stats-v-cards", styles);
        Assert.Contains("ccs-ending-stats-v-strip", styles);
        Assert.Contains("ccs-ending-stats-v-bold", styles);
        Assert.Contains("ccs-ending-stats-v-outline", styles);
        Assert.Contains("ccs-ending-stats-v-solid", styles);
        Assert.Contains("ccs-ending-stats-v-gradient", styles);
        Assert.Contains("ccs-ending-stats-v-compact", styles);
        Assert.Contains("container-type", styles);
    }

    [Fact]
    public void CanvasRuntime_RegistersSocialsWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("socials:", runtime);
        Assert.Contains("createSocialsEl", runtime);
        Assert.Contains("updateSocials", runtime);
        Assert.Contains("SOCIALS_PLATFORMS", runtime);
        Assert.Contains("iconLibrary", runtime);
        Assert.Contains("fontawesome", runtime);
        Assert.Contains("customIconUrl", runtime);
        Assert.Contains("platform", runtime);
        Assert.Contains("resolveSocialsEntries", runtime);
        Assert.Contains("ResizeObserver", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"socials\"", editor);
        Assert.Contains("selectProp(\"variant\"", editor);
        Assert.Contains("selectProp(\"iconLibrary\"", editor);
        Assert.Contains("selectProp(\"platform\"", editor);
        Assert.Contains("Font Awesome", editor);
        Assert.Contains("textProp(\"handle\"", editor);
        Assert.Contains("Row", editor);
        Assert.Contains("Pills", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-socials", styles);
        Assert.Contains("ccs-socials-v-row", styles);
        Assert.Contains("ccs-socials-v-pills", styles);
        Assert.Contains("ccs-socials-v-cards", styles);
        Assert.Contains("ccs-socials-v-stack", styles);
        Assert.Contains("ccs-socials-v-neon", styles);
        Assert.Contains("ccs-socials-v-minimal", styles);
        Assert.Contains("ccs-socials-icon", styles);
        Assert.Contains("container-type", styles);
    }

    [Fact]
    public void ListShapeTypes_ContainsFrames()
    {
        IReadOnlyList<string> types = CanvasOverlayAssets.ListShapeTypes();
        Assert.Contains("frame.neon", types);
        Assert.Contains("frame.card", types);
        Assert.Contains("shape.vignette", types);
        Assert.Contains("shape.scene-bg", types);
    }

    [Fact]
    public void CanvasRuntime_RegistersCardFrameShape()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"frame.card\":", runtime);
        Assert.Contains("CARD_FRAME_SIZE_PRESETS", runtime);
        Assert.Contains("applyCardFrame", runtime);
        Assert.Contains("ccs-frame-card-sweep", runtime);
        Assert.Contains("ccs-frame-card-topline", runtime);
        Assert.Contains("ccs-frame-card-bottomline", runtime);
        Assert.Contains("sizePreset", runtime);
        Assert.Contains("color2", runtime);
        Assert.Contains("fillOpacity", runtime);
        Assert.Contains("showSweep", runtime);
        Assert.Contains("showLines", runtime);
        Assert.Contains("chatting", runtime);
        Assert.Contains("metaschutz", runtime);
        Assert.Contains("ending", runtime);
        Assert.Contains("--frame-color2", runtime);
        Assert.Contains("--frame-fill-opacity", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"frame.card\"", editor);
        Assert.Contains("selectProp(\"variant\"", editor);
        Assert.Contains("selectProp(\"sizePreset\"", editor);
        Assert.Contains("color2", editor);
        Assert.Contains("fillOpacity", editor);
        Assert.Contains("CARD_FRAME_SIZE_PRESETS", editor);
        Assert.Contains("Cyber", editor);
        Assert.Contains("Soft", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-frame-card", styles);
        Assert.Contains("ccs-frame-card-v-classic", styles);
        Assert.Contains("ccs-frame-card-v-neon", styles);
        Assert.Contains("ccs-frame-card-v-soft", styles);
        Assert.Contains("ccs-frame-card-v-bold", styles);
        Assert.Contains("ccs-frame-card-v-outline", styles);
        Assert.Contains("ccs-frame-card-v-glass", styles);
        Assert.Contains("ccs-frame-card-v-cyber", styles);
        Assert.Contains("ccs-frame-card-v-minimal", styles);
        Assert.Contains("ccs-frame-card-sweep", styles);
        Assert.Contains("--frame-color2", styles);
        Assert.Contains("--frame-fill-opacity", styles);

        Assert.True(CanvasOverlayAssets.TryGet("solo/solo.js", out string solo, out _));
        Assert.Contains("frame.", solo);
    }

    [Fact]
    public void CanvasRuntime_RegistersSceneBackgroundShape()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"shape.scene-bg\":", runtime);
        Assert.Contains("applySceneBg", runtime);
        Assert.Contains("SCENE_BG_PRESETS", runtime);
        Assert.Contains("ember", runtime);
        Assert.Contains("crimson", runtime);
        Assert.Contains("aurora", runtime);
        Assert.Contains("violet", runtime);
        Assert.Contains("gold", runtime);
        Assert.Contains("ice", runtime);
        Assert.Contains("lime", runtime);
        Assert.Contains("magenta", runtime);
        Assert.Contains("steel", runtime);
        Assert.Contains("inferno", runtime);
        Assert.Contains("driftDuration", runtime);
        Assert.Contains("particleDuration", runtime);
        Assert.Contains("--ccs-bg-glow1", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"shape.scene-bg\"", editor);
        Assert.Contains("selectProp(\"preset\"", editor);
        Assert.Contains("glow1", editor);
        Assert.Contains("speed", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-shape-scene-bg", styles);
        Assert.Contains("ccs-scene-bg-drift", styles);
        Assert.Contains("ccs-scene-particle-drift", styles);
        Assert.Contains("--ccs-bg-base", styles);

        Assert.True(CanvasOverlayAssets.TryGet("solo/solo.js", out string solo, out _));
        Assert.Contains("shape.scene-bg", solo);
    }

    [Fact]
    public void EditorChrome_ShowsFrameOnAllItems_SelectionKeepsAccent()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("edit-chrome", runtime);
        Assert.Contains("editing ? \" edit-chrome\" : \"\"", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-item.edit-chrome", styles);
        Assert.Contains(".ccs-item.editing", styles);
        Assert.Contains("outline: 2px solid var(--accent)", styles);

        Assert.True(CanvasOverlayAssets.TryGet("view/view.js", out string view, out _));
        Assert.Contains("editing: false", view);
    }

    [Fact]
    public void EditorProps_MutateLiveLayoutItem_NotStaleClosure()
    {
        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("function liveItem(", editor);
        Assert.Contains("function commitProp(", editor);
        Assert.Contains("commitProp(item,", editor);
        Assert.Contains("liveItem(from)", editor);

        // Geometry fields already used selectedItem(); widget props must too after layout WS echo.
        Assert.DoesNotContain("item.props[key] = select.value;", editor);
        Assert.DoesNotContain("item.props[key] = e.target.checked;", editor);
        Assert.DoesNotContain("item.props[key] = Number(input.value);", editor);
        Assert.DoesNotContain("item.props[key] = input.value;", editor);
    }

    [Fact]
    public void Runtime_ExposesEffectStrategiesAndApplyItemEffects()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("EFFECT_STRATEGIES", runtime);
        Assert.Contains("applyItemEffects", runtime);
        Assert.Contains("registerEffect", runtime);
        Assert.Contains("listEffectTypes", runtime);
        Assert.Contains("glow", runtime);
        Assert.Contains("particles", runtime);
        Assert.Contains("scanlines", runtime);
        Assert.Contains("vignette", runtime);
        Assert.Contains("blur", runtime);
        Assert.Contains("noise", runtime);
        Assert.Contains("effects: []", runtime);
        Assert.Contains("loadExtensions", runtime);
        Assert.Contains("extUrl", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains("ccs-item-fx-layer", styles);
        Assert.Contains("ccs-item-fx-glow", styles);
        Assert.Contains("ccs-item-fx-scanlines", styles);
    }

    [Fact]
    public void Editor_HasSectionsFontColorAndEffectsPanel()
    {
        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("function propSection", editor);
        Assert.Contains("function featureSection", editor);
        Assert.Contains("function fontProp", editor);
        Assert.Contains("function colorProp", editor);
        Assert.Contains("renderEffectsPanel", editor);
        Assert.Contains("ccs-prop-section", editor);
        Assert.Contains("Position", editor);
        Assert.Contains("addBtn.textContent", editor);
        Assert.Contains("Effekt", editor);
    }

    [Fact]
    public void Runtime_SetLayoutKeepSelection_RebindsSelect()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("function setLayout(next, keepSelection)", runtime);
        Assert.Contains("keepSelection && selectedId", runtime);
        Assert.Contains("select(selectedId)", runtime);
    }
}
