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
    public void ListWidgetTypes_ContainsOnlineAlertMusicChatEndingStatsTextImageCountdownSocialsPartnerRoulette()
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
        Assert.Contains("partner-roulette", types);
        Assert.Contains("goal-bar", types);
        Assert.Contains("event-ticker", types);
        Assert.Contains("viewer-count", types);
        Assert.Contains("lower-third", types);
        Assert.Contains("qr-code", types);
        Assert.Contains("brb-panel", types);
        Assert.Contains("announcement-bar", types);
        Assert.Contains("animated-background", types);
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
        Assert.Contains("lookSection(\"music\"", editor);

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
    public void CanvasRuntime_RegistersPartnerRouletteWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"partner-roulette\":", runtime);
        Assert.Contains("createPartnerRouletteEl", runtime);
        Assert.Contains("updatePartnerRoulette", runtime);
        Assert.Contains("PARTNER_ROULETTE_TRANSITIONS", runtime);
        Assert.Contains("intervalMs", runtime);
        Assert.Contains("transitionMs", runtime);
        Assert.Contains("resolvePartnerRouletteImages", runtime);
        Assert.Contains("ccs-partner-roulette", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"partner-roulette\"", editor);
        Assert.Contains("selectProp(\"transition\"", editor);
        Assert.Contains("intervalMs", editor);
        Assert.Contains("transitionMs", editor);
        Assert.Contains("Bild hinzuf\\xFCgen", editor);
        Assert.Contains("Partner Roulette", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-partner-roulette", styles);
        Assert.Contains(".ccs-partner-roulette-slide", styles);
        Assert.Contains("ccs-partner-roulette-t-fade", styles);
        Assert.Contains("ccs-partner-roulette-t-slide", styles);
        Assert.Contains("ccs-partner-roulette-t-crossfade", styles);
        Assert.Contains("--ccs-roulette-transition-ms", styles);
    }

    [Fact]
    public void ListShapeTypes_ContainsFrames()
    {
        IReadOnlyList<string> types = CanvasOverlayAssets.ListShapeTypes();
        Assert.Contains("frame", types);
        Assert.DoesNotContain("frame.rect", types);
        Assert.DoesNotContain("frame.neon", types);
        Assert.DoesNotContain("frame.circle", types);
        Assert.DoesNotContain("frame.corners", types);
        Assert.DoesNotContain("frame.bevel", types);
        Assert.DoesNotContain("frame.dashed", types);
        Assert.Contains("frame.card", types);
        Assert.Contains("shape.vignette", types);
        Assert.Contains("shape.scene-bg", types);
        Assert.Contains("shape.cutout", types);
        Assert.Contains("shape.divider", types);
        Assert.Contains("shape.cam-ring", types);
        Assert.Contains("shape.sticker", types);
    }

    [Fact]
    public void CanvasRuntime_RegistersUnifiedFrameShape()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("frame:", runtime);
        Assert.Contains("FRAME_MODES", runtime);
        Assert.Contains("createFrameEl", runtime);
        Assert.Contains("applyFrame", runtime);
        Assert.Contains("resolveFrameMode", runtime);
        Assert.Contains("ccs-frame-m-", runtime);
        Assert.Contains("\"rect\"", runtime);
        Assert.Contains("\"circle\"", runtime);
        Assert.Contains("\"corners\"", runtime);
        Assert.Contains("\"bevel\"", runtime);
        Assert.Contains("\"neon\"", runtime);
        Assert.Contains("\"dashed\"", runtime);
        Assert.Contains("\"double\"", runtime);
        Assert.Contains("\"dotted\"", runtime);
        Assert.Contains("\"groove\"", runtime);
        Assert.Contains("\"ridge\"", runtime);
        Assert.Contains("\"pixel\"", runtime);
        Assert.Contains("\"ticket\"", runtime);
        Assert.Contains("\"stamp\"", runtime);
        Assert.Contains("\"film\"", runtime);
        Assert.Contains("\"hud\"", runtime);
        Assert.Contains("\"hex\"", runtime);
        Assert.Contains("\"octagon\"", runtime);
        Assert.Contains("\"tape\"", runtime);
        Assert.Contains("\"scan\"", runtime);
        Assert.Contains("\"rainbow\"", runtime);
        Assert.Contains("\"comic\"", runtime);
        Assert.Contains("\"frosted\"", runtime);
        Assert.Contains("\"chrome\"", runtime);
        Assert.Contains("\"notch\"", runtime);
        Assert.Contains("\"brackets\"", runtime);
        Assert.Contains("\"orbit\"", runtime);
        Assert.Contains("--frame-radius", runtime);
        // Legacy types remain renderable as mode aliases
        Assert.Contains("frame.rect", runtime);
        Assert.Contains("frame.neon", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"frame\"", editor);
        Assert.Contains("selectProp(\"mode\"", editor);
        Assert.Contains("numProp(\"radius\"", editor);
        Assert.Contains("FRAME_MODES", editor);
        Assert.DoesNotContain("type: \"frame.rect\"", editor);
        Assert.DoesNotContain("type: \"frame.neon\"", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-frame", styles);
        Assert.Contains(".ccs-frame-m-rect", styles);
        Assert.Contains(".ccs-frame-m-neon", styles);
        Assert.Contains(".ccs-frame-m-double", styles);
        Assert.Contains(".ccs-frame-m-pixel", styles);
        Assert.Contains(".ccs-frame-m-ticket", styles);
        Assert.Contains(".ccs-frame-m-hud", styles);
        Assert.Contains(".ccs-frame-m-hex", styles);
        Assert.Contains(".ccs-frame-m-rainbow", styles);
        Assert.Contains(".ccs-frame-m-orbit", styles);
        Assert.Contains("--frame-radius", styles);

        Assert.True(CanvasOverlayAssets.TryGet("solo/solo.js", out string solo, out _));
        Assert.Contains("frame", solo);
    }

    [Fact]
    public void CanvasRuntime_RegistersCutoutShape()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"shape.cutout\":", runtime);
        Assert.Contains("createCutoutEl", runtime);
        Assert.Contains("applyCutout", runtime);
        Assert.Contains("applyCutoutStackMask", runtime);
        Assert.Contains("ccs-shape-cutout", runtime);
        Assert.Contains("ccs-item-cutout", runtime);
        Assert.Contains("ccs-cutout-stack", runtime);
        Assert.Contains("--cutout-radius", runtime);
        Assert.Contains("maskUnits", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"shape.cutout\"", editor);
        Assert.Contains("numProp(\"radius\"", editor);
        Assert.Contains("Cutout", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-shape-cutout", styles);
        Assert.Contains(".ccs-item-cutout", styles);
        Assert.Contains(".ccs-cutout-stack", styles);
        Assert.Contains("--cutout-radius", styles);
        Assert.DoesNotContain("mix-blend-mode: destination-out", styles);

        Assert.True(CanvasOverlayAssets.TryGet("solo/solo.js", out string solo, out _));
        Assert.Contains("startsWith(\"shape.\")", solo);
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
        Assert.Contains("motion", runtime);
        Assert.Contains("ccs-item-fx-glow--", runtime);
        Assert.Contains("ccs-item-glow-content--", runtime);
        Assert.Contains("contentGlowFilter", runtime);
        Assert.Contains("pulse", runtime);
        Assert.Contains("breathe", runtime);
        Assert.Contains("particles", runtime);
        Assert.Contains("scanlines", runtime);
        Assert.Contains("vignette", runtime);
        Assert.Contains("blur", runtime);
        Assert.Contains("noise", runtime);
        Assert.Contains("neon", runtime);
        Assert.Contains("glitch", runtime);
        Assert.Contains("sparkle", runtime);
        Assert.Contains("aurora", runtime);
        Assert.Contains("pulse-ring", runtime);
        Assert.Contains("hologram", runtime);
        Assert.Contains("outline", runtime);
        Assert.Contains("drop-shadow", runtime);
        Assert.Contains("rainbow", runtime);
        Assert.Contains("spotlight", runtime);
        Assert.Contains("effects: []", runtime);
        Assert.Contains("loadExtensions", runtime);
        Assert.Contains("extUrl", runtime);
        Assert.Contains("resolveEffectTarget", runtime);
        Assert.Contains("effectTargets", runtime);
        Assert.Contains("fxTarget", runtime);
        Assert.Contains("\"content\"", runtime);
        Assert.Contains("targets: [\"box\", \"content\"]", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("Effekt-Ziel", editor);
        Assert.Contains("\"Inhalt\"", editor);
        Assert.Contains("effectTargets", editor);
        Assert.Contains("allowedTargets", editor);
        Assert.Contains("target: \"box\"", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains("ccs-item-fx-layer", styles);
        Assert.Contains("ccs-item-fx-glow", styles);
        Assert.Contains("ccs-item-fx-glow--pulse", styles);
        Assert.Contains("ccs-item-fx-glow--breathe", styles);
        Assert.Contains("ccs-fx-glow-pulse", styles);
        Assert.Contains("ccs-item-glow-content--pulse", styles);
        Assert.Contains("ccs-item-shadow-content--pulse", styles);
        Assert.Contains("--ccs-fx-glow-i", styles);
        Assert.Contains("ccs-item-outline--pulse", styles);
        Assert.Contains("ccs-item-fx-scanlines", styles);
        Assert.Contains("ccs-item-fx-neon", styles);
        Assert.Contains("ccs-item-fx-glitch", styles);
        Assert.Contains("ccs-item-fx-sparkle", styles);
        Assert.Contains("ccs-item-fx-aurora", styles);
        Assert.Contains("ccs-item-fx-pulse-ring", styles);
        Assert.Contains("ccs-item-fx-hologram", styles);
        Assert.Contains("ccs-item-fx-outline", styles);
        Assert.Contains("ccs-item-fx-drop-shadow", styles);
        Assert.Contains("ccs-item-fx-rainbow", styles);
        Assert.Contains("ccs-item-fx-spotlight", styles);
    }

    [Fact]
    public void Runtime_ExposesAnimationStrategiesAndApplyItemAnimations()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("ANIMATION_STRATEGIES", runtime);
        Assert.Contains("applyItemAnimations", runtime);
        Assert.Contains("registerAnimation", runtime);
        Assert.Contains("listAnimationTypes", runtime);
        Assert.Contains("animations: []", runtime);
        Assert.Contains("fade", runtime);
        Assert.Contains("slide", runtime);
        Assert.Contains("bounce", runtime);
        Assert.Contains("pop", runtime);
        Assert.Contains("shake", runtime);
        Assert.Contains("float", runtime);
        Assert.Contains("pulse", runtime);
        Assert.Contains("spin", runtime);
        Assert.Contains("wiggle", runtime);
        Assert.Contains("flip", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains("ccs-item-anim-target", styles);
        Assert.Contains("ccs-anim-fade", styles);
        Assert.Contains("ccs-anim-slide", styles);
        Assert.Contains("ccs-anim-bounce", styles);
        Assert.Contains("ccs-anim-pop", styles);
        Assert.Contains("ccs-anim-shake", styles);
        Assert.Contains("ccs-anim-float", styles);
        Assert.Contains("ccs-anim-pulse", styles);
        Assert.Contains("ccs-anim-spin", styles);
        Assert.Contains("ccs-anim-wiggle", styles);
        Assert.Contains("ccs-anim-flip", styles);
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
        Assert.Contains("renderAnimationsPanel", editor);
        Assert.Contains("ccs-prop-section", editor);
        Assert.Contains("ccs-prop-section-chevron", editor);
        Assert.Contains("ccs-num-prop-label", editor);
        Assert.Contains("Position", editor);
        Assert.Contains("addBtn.textContent", editor);
        Assert.Contains("Effekt", editor);
        Assert.Contains("Animation", editor);
        Assert.Contains("wireInspectorTabs", editor);
        Assert.Contains("ccs-props-tab", editor);
        Assert.Contains("propEffects", editor);
        Assert.Contains("propAnimations", editor);
    }

    [Fact]
    public void EditorHtml_HasInspectorTabs()
    {
        Assert.True(CanvasOverlayAssets.TryGet("editor/index.html", out string html, out _));
        Assert.Contains("id=\"propsTabs\"", html);
        Assert.Contains("data-tab=\"layout\"", html);
        Assert.Contains("data-tab=\"widget\"", html);
        Assert.Contains("data-tab=\"effects\"", html);
        Assert.Contains("data-tab=\"animations\"", html);
        Assert.Contains("id=\"propEffects\"", html);
        Assert.Contains("id=\"propAnimations\"", html);
        Assert.Contains("id=\"propsPaneLayout\"", html);
    }

    [Fact]
    public void EditorHtml_CanvasSizeModalClosedByDefault()
    {
        Assert.True(CanvasOverlayAssets.TryGet("editor/index.html", out string html, out _));
        Assert.Contains(
            "id=\"btnCanvasSize\" class=\"ccs-toolbar-size\"",
            html);
        Assert.Contains(
            "aria-controls=\"canvasSizeModal\"",
            html);
        Assert.Contains(
            "<dialog id=\"canvasSizeModal\" class=\"ccs-modal\"",
            html);
        Assert.Contains("Fenstergröße", html);
        Assert.Contains("id=\"canvasSizePreset\"", html);
        Assert.DoesNotContain("<dialog id=\"canvasSizeModal\" open", html);
    }

    [Fact]
    public void EditorHtml_ToolbarUsesIconButtons()
    {
        Assert.True(CanvasOverlayAssets.TryGet("editor/index.html", out string html, out _));
        Assert.Contains("class=\"ccs-toolbar-btn\"", html);
        Assert.Contains("aria-label=\"Löschen\"", html);
        Assert.Contains("aria-label=\"Duplizieren\"", html);
        Assert.Contains("aria-label=\"Ganz nach oben\"", html);
        Assert.Contains("aria-label=\"Ebene rauf\"", html);
        Assert.Contains("aria-label=\"Ebene runter\"", html);
        Assert.Contains("aria-label=\"Ganz nach unten\"", html);
        Assert.Contains("title=\"OBS-Vorschau\"", html);
        Assert.Contains("title=\"Raster\"", html);
        Assert.Contains("title=\"Einrasten an Raster\"", html);
        Assert.Contains("title=\"Magnet\"", html);
        Assert.DoesNotContain(">Löschen</button>", html);
        Assert.DoesNotContain(">Duplizieren</button>", html);
    }

    [Fact]
    public void Editor_DeleteKeyRemovesSelectedItem()
    {
        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("keydown", editor);
        Assert.Contains("\"Delete\"", editor);
        Assert.Contains("\"Backspace\"", editor);
        Assert.Contains("runEditorCommand(\"delete\"", editor);
        Assert.Contains("isEditableKeyboardTarget", editor);
    }

    [Fact]
    public void Runtime_SetLayoutKeepSelection_RebindsSelect()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("function setLayout(next, keepSelection)", runtime);
        Assert.Contains("keepSelection && selectedId", runtime);
        Assert.Contains("select(selectedId)", runtime);
    }

    [Fact]
    public void Runtime_InvokesOnAfterRenderAfterRebuild()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("onAfterRender", runtime);
        Assert.Contains("ccs-editor-grid", runtime);
        Assert.Contains("ccs-obs-preview", runtime);
        Assert.Contains("ccs-magnet-guides", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("onAfterRender", editor);
        Assert.Contains("applyEditorLayers", editor);
    }

    [Fact]
    public void Editor_ResizeSnapsActiveHandleEdges()
    {
        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("activeEdgesFromHandle", editor);
        Assert.Contains("applyGridSnap", editor);
        Assert.Contains("mode === \"resize\"", editor);
        Assert.Contains("active.right", editor);
        Assert.Contains("active.bottom", editor);
        Assert.Contains("active.left", editor);
        Assert.Contains("active.top", editor);
    }

    [Fact]
    public void CanvasRuntime_RegistersGoalBarWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"goal-bar\":", runtime);
        Assert.Contains("createGoalBarEl", runtime);
        Assert.Contains("updateGoalBar", runtime);
        Assert.Contains("GOAL_BAR_VARIANTS", runtime);
        Assert.Contains("GOAL_BAR_SIZE_PRESETS", runtime);
        Assert.Contains("ccs-goal-bar", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"goal-bar\"", editor);
        Assert.Contains("Goal Bar", editor);
        Assert.Contains("appendGoalBarProps", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-goal-bar", styles);
        Assert.Contains("ccs-goal-bar-v-neon", styles);
        Assert.Contains("ccs-goal-bar-v-capsule", styles);
    }

    [Fact]
    public void CanvasRuntime_RegistersEventTickerWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"event-ticker\":", runtime);
        Assert.Contains("createEventTickerEl", runtime);
        Assert.Contains("updateEventTicker", runtime);
        Assert.Contains("pushEventTickerItem", runtime);
        Assert.Contains("EVENT_TICKER_VARIANTS", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"event-ticker\"", editor);
        Assert.Contains("Event Ticker", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-event-ticker", styles);
    }

    [Fact]
    public void CanvasRuntime_RegistersViewerCountWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"viewer-count\":", runtime);
        Assert.Contains("createViewerCountEl", runtime);
        Assert.Contains("updateViewerCount", runtime);
        Assert.Contains("VIEWER_COUNT_VARIANTS", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"viewer-count\"", editor);
        Assert.Contains("Viewer Count", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-viewer-count", styles);
    }

    [Fact]
    public void CanvasRuntime_RegistersLowerThirdWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"lower-third\":", runtime);
        Assert.Contains("createLowerThirdEl", runtime);
        Assert.Contains("updateLowerThird", runtime);
        Assert.Contains("LOWER_THIRD_VARIANTS", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"lower-third\"", editor);
        Assert.Contains("Lower Third", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-lower-third", styles);
        Assert.Contains("ccs-lower-third-v-broadcast", styles);
    }

    [Fact]
    public void CanvasRuntime_RegistersQrCodeWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"qr-code\":", runtime);
        Assert.Contains("createQrCodeEl", runtime);
        Assert.Contains("updateQrCode", runtime);
        Assert.Contains("QR_CODE_VARIANTS", runtime);
        Assert.Contains("encodeQrSvg", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"qr-code\"", editor);
        Assert.Contains("QR Code", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-qr-code", styles);
    }

    [Fact]
    public void CanvasRuntime_RegistersBrbPanelWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"brb-panel\":", runtime);
        Assert.Contains("createBrbPanelEl", runtime);
        Assert.Contains("updateBrbPanel", runtime);
        Assert.Contains("paintBrbPanel", runtime);
        Assert.Contains("BRB_PANEL_VARIANTS", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"brb-panel\"", editor);
        Assert.Contains("BRB Panel", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-brb-panel", styles);
    }

    [Fact]
    public void CanvasRuntime_RegistersAnnouncementBarWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"announcement-bar\":", runtime);
        Assert.Contains("createAnnouncementBarEl", runtime);
        Assert.Contains("updateAnnouncementBar", runtime);
        Assert.Contains("ANNOUNCEMENT_BAR_VARIANTS", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"announcement-bar\"", editor);
        Assert.Contains("Announcement Bar", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-announcement-bar", styles);
        Assert.Contains("ccs-announcement-bar-v-ribbon", styles);
    }

    [Fact]
    public void CanvasRuntime_RegistersAnimatedBackgroundWidget()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"animated-background\":", runtime);
        Assert.Contains("createAnimatedBackgroundEl", runtime);
        Assert.Contains("updateAnimatedBackground", runtime);
        Assert.Contains("ANIMATED_BACKGROUND_VARIANTS", runtime);
        Assert.Contains("ANIMATED_BACKGROUND_SIZE_PRESETS", runtime);
        Assert.Contains("\"hacker\"", runtime);
        Assert.Contains("syncMatrixBackground", runtime);
        Assert.Contains("MATRIX_GLYPHS", runtime);
        Assert.Contains("\\uFF71\\uFF72\\uFF73\\uFF74\\uFF75", runtime);
        Assert.Contains("\"retro\"", runtime);
        Assert.Contains("\"meme\"", runtime);
        Assert.Contains("\"queer\"", runtime);
        Assert.Contains("\"peace\"", runtime);
        Assert.Contains("\"street\"", runtime);
        Assert.Contains("\"mountains\"", runtime);
        Assert.Contains("\"alpine\"", runtime);
        Assert.Contains("\"fuji\"", runtime);
        Assert.Contains("\"neon-peaks\"", runtime);
        Assert.Contains("\"ridge-storm\"", runtime);
        Assert.Contains("syncParallaxBackground", runtime);
        Assert.Contains("requestAnimationFrame", runtime);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(runtime, "\"(?:hacker|cyber|retro|vaporwave|meme|queer|peace|street|aurora|ocean|fire|cosmic|glitch|pixel|lava|ice|disco|rain|bubbles|grid|sunset|forest|candy|noir|mountains|alpine|fuji|mesa|neon-peaks|mist-peaks|lowpoly|papercut|floating|ridge-storm)\"").Count >= 34);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"animated-background\"", editor);
        Assert.Contains("Animated Background", editor);
        Assert.Contains("ANIMATED_BACKGROUND_VARIANTS", editor);
        Assert.Contains("lookSection(\"animated-background\")", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-animated-bg", styles);
        Assert.Contains("ccs-animated-bg-v-hacker", styles);
        Assert.Contains(".ccs-abg-matrix", styles);
        Assert.Contains("ccs-animated-bg-v-queer", styles);
        Assert.Contains("ccs-animated-bg-v-street", styles);
        Assert.Contains("ccs-animated-bg-v-mountains", styles);
        Assert.Contains("ccs-animated-bg-v-fuji", styles);
        Assert.Contains("ccs-animated-bg-v-ridge-storm", styles);
        Assert.Contains(".ccs-abg-parallax", styles);
        Assert.Contains("--ccs-abg-speed", styles);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(styles, @"ccs-animated-bg-v-(?:hacker|cyber|retro|vaporwave|meme|queer|peace|street|aurora|ocean|fire|cosmic|glitch|pixel|lava|ice|disco|rain|bubbles|grid|sunset|forest|candy|noir|mountains|alpine|fuji|mesa|neon-peaks|mist-peaks|lowpoly|papercut|floating|ridge-storm)").Count >= 34);
    }

    [Fact]
    public void CanvasRuntime_RegistersDividerCamRingStickerShapes()
    {
        Assert.True(CanvasOverlayAssets.TryGet("shared/runtime.js", out string runtime, out _));
        Assert.Contains("\"shape.divider\":", runtime);
        Assert.Contains("\"shape.cam-ring\":", runtime);
        Assert.Contains("\"shape.sticker\":", runtime);
        Assert.Contains("createDividerEl", runtime);
        Assert.Contains("createCamRingEl", runtime);
        Assert.Contains("createStickerEl", runtime);
        Assert.Contains("DIVIDER_VARIANTS", runtime);
        Assert.Contains("CAM_RING_VARIANTS", runtime);
        Assert.Contains("STICKER_PRESETS", runtime);

        Assert.True(CanvasOverlayAssets.TryGet("editor/editor.js", out string editor, out _));
        Assert.Contains("type: \"shape.divider\"", editor);
        Assert.Contains("type: \"shape.cam-ring\"", editor);
        Assert.Contains("type: \"shape.sticker\"", editor);
        Assert.Contains("Divider", editor);
        Assert.Contains("Cam Ring", editor);
        Assert.Contains("Sticker", editor);

        Assert.True(CanvasOverlayAssets.TryGet("shared/styles.css", out string styles, out _));
        Assert.Contains(".ccs-divider", styles);
        Assert.Contains(".ccs-cam-ring", styles);
        Assert.Contains(".ccs-sticker", styles);
    }
}
