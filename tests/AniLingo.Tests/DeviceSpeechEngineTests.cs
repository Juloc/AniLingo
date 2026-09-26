using Jint;

namespace AniLingo.Tests;

/// <summary>
/// Runs the browser speech engine (wwwroot/js/tts.js) against a scripted Web Speech
/// fake, so queue bounds, cancellation and voice resolution are verified as shipped.
/// </summary>
[TestClass]
public sealed class DeviceSpeechEngineTests
{
    private const string Harness = """
        var window = globalThis;
        class EventTarget {
            constructor() { this.__listeners = {}; }
            addEventListener(type, listener) {
                (this.__listeners[type] = this.__listeners[type] || []).push(listener);
            }
            removeEventListener(type, listener) {
                this.__listeners[type] = (this.__listeners[type] || []).filter(x => x !== listener);
            }
            dispatchEvent(event) {
                (this.__listeners[event.type] || []).slice().forEach(listener => listener.call(this, event));
                return true;
            }
        }
        class CustomEvent {
            constructor(type, init) { this.type = type; this.detail = init ? init.detail : undefined; }
        }
        window.setTimeout = () => 0;
        window.clearTimeout = () => {};
        window.SpeechSynthesisUtterance = class { constructor(text) { this.text = text; } };
        window.speechSynthesis = {
            voices: [{ voiceURI: "de-voice", name: "Deutsch", lang: "de-DE", default: true, localService: true }],
            queue: [],
            spoken: [],
            maxQueued: 0,
            cancelCount: 0,
            getVoices() { return this.voices; },
            speak(utterance) {
                this.queue.push(utterance);
                this.spoken.push(utterance.text);
                this.maxQueued = Math.max(this.maxQueued, this.queue.length);
                if (this.queue.length === 1 && utterance.onstart) utterance.onstart();
            },
            finishCurrent() {
                const current = this.queue.shift();
                if (current && current.onend) current.onend();
                if (this.queue.length && this.queue[0].onstart) this.queue[0].onstart();
            },
            cancel() {
                const cancelled = this.queue;
                this.queue = [];
                this.cancelCount++;
                cancelled.forEach(u => u.onerror && u.onerror({ error: "interrupted" }));
            },
            pause() {},
            resume() {}
        };
        """;

    [TestMethod]
    public void SequenceKeepsTheQueueBoundedAndPullsItemsLazily()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var pulled = 0;
            var events = { itemstart: [], complete: 0 };
            function* paragraphs() {
                for (let i = 0; i < 10; i++) {
                    pulled++;
                    yield { key: i, text: "Absatz " + i + ".", language: "de" };
                }
            }
            var provider = AniLingoTts.createDeviceProvider();
            provider.addEventListener("itemstart", e => events.itemstart.push(e.detail.item.key));
            provider.addEventListener("complete", () => events.complete++);
            var result = null;
            provider.speakSequence(paragraphs(), { lookAhead: 2 }).then(r => { result = r; });
            """);
        engine.Advanced.ProcessTasks();

        Assert.AreEqual(3, Number(engine, "speechSynthesis.queue.length"));
        Assert.IsTrue(Number(engine, "pulled") <= 4, "Items must be pulled lazily.");

        for (var step = 0; step < 10; step++)
        {
            engine.Execute("speechSynthesis.finishCurrent();");
            engine.Advanced.ProcessTasks();
            Assert.IsTrue(Number(engine, "speechSynthesis.queue.length") <= 3);
            Assert.IsTrue(
                Number(engine, "pulled") <= step + 5,
                "Pulled items must stay within the look-ahead window.");
        }

        Assert.AreEqual(3, Number(engine, "speechSynthesis.maxQueued"));
        Assert.AreEqual(10, Number(engine, "speechSynthesis.spoken.length"));
        Assert.AreEqual("0,1,2,3,4,5,6,7,8,9", engine.Evaluate("events.itemstart.join(',')").AsString());
        Assert.AreEqual(1, Number(engine, "events.complete"));
        Assert.IsTrue(engine.Evaluate("result.completed === true").AsBoolean());
        Assert.AreEqual("idle", engine.Evaluate("provider.state").AsString());
    }

    [TestMethod]
    public void StopCancelsTheSessionAndNothingElseIsQueued()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var completeCount = 0;
            var items = [];
            for (let i = 0; i < 10; i++) items.push({ key: i, text: "Satz " + i + ".", language: "de" });
            var provider = AniLingoTts.createDeviceProvider();
            provider.addEventListener("complete", () => completeCount++);
            var result = null;
            provider.speakSequence(items, { lookAhead: 1 }).then(r => { result = r; });
            """);
        engine.Advanced.ProcessTasks();
        engine.Execute("speechSynthesis.finishCurrent();");
        engine.Advanced.ProcessTasks();

        var spokenBeforeStop = Number(engine, "speechSynthesis.spoken.length");
        engine.Execute("provider.stop();");
        engine.Advanced.ProcessTasks();
        engine.Execute("speechSynthesis.finishCurrent();");
        engine.Advanced.ProcessTasks();

        Assert.AreEqual(0, Number(engine, "speechSynthesis.queue.length"));
        Assert.AreEqual(spokenBeforeStop, Number(engine, "speechSynthesis.spoken.length"));
        Assert.IsTrue(Number(engine, "speechSynthesis.cancelCount") >= 2);
        Assert.IsTrue(engine.Evaluate("result !== null && result.completed === false").AsBoolean());
        Assert.AreEqual(0, Number(engine, "completeCount"));
        Assert.AreEqual("idle", engine.Evaluate("provider.state").AsString());
    }

    [TestMethod]
    public void BrowserResolverMirrorsServerVoiceRulesAndReasons()
    {
        var engine = CreateEngine();
        engine.Execute("""
            var device = { id: "device", kind: "device", isAvailable: true,
                capabilities: { platformDefaultVoice: true } };
            var offline = { id: "offline", kind: "offline-neural", isAvailable: true,
                capabilities: { platformDefaultVoice: false } };
            var voices = [
                { providerId: "device", voiceId: "de-at", name: "Deutsch AT", language: "de-AT", isDefault: true },
                { providerId: "device", voiceId: "de-de", name: "Deutsch DE", language: "de-DE" },
                { providerId: "device", voiceId: "ja-1", name: "Japanisch", language: "ja-JP" }
            ];
            var r = AniLingoTts.resolveSpeech;
            var exact = r({ language: "de-DE" }, [device], voices).resolution;
            var baseMatch = r({ language: "de-CH" }, [device], voices).resolution;
            var selected = r({ providerId: "device", voiceId: "ja-1", language: "ja" }, [device], voices).resolution;
            var wrongLanguage = r({ providerId: "device", voiceId: "ja-1", language: "de-DE" }, [device], voices).resolution;
            var platformDefault = r({ language: "ko" }, [device], voices).resolution;
            var noProvider = r({ language: "de" }, [], voices);
            var unavailable = r({ language: "de" }, [{ ...device, isAvailable: false }], voices);
            var noVoice = r({ language: "ko" }, [offline], voices);
            """);

        Assert.AreEqual("de-de", engine.Evaluate("exact.voiceId").AsString());
        Assert.AreEqual("device-fallback", engine.Evaluate("exact.reason").AsString());
        Assert.AreEqual("de-at", engine.Evaluate("baseMatch.voiceId").AsString());
        Assert.AreEqual("ja-1", engine.Evaluate("selected.voiceId").AsString());
        Assert.AreEqual("selected-provider", engine.Evaluate("selected.reason").AsString());
        Assert.AreEqual("de-de", engine.Evaluate("wrongLanguage.voiceId").AsString());
        Assert.IsTrue(engine.Evaluate("platformDefault.usesProviderDefaultVoice").AsBoolean());
        Assert.IsTrue(engine.Evaluate("platformDefault.voiceId === null").AsBoolean());
        Assert.AreEqual("no-provider", engine.Evaluate("noProvider.unavailableReason").AsString());
        Assert.AreEqual("provider-unavailable", engine.Evaluate("unavailable.unavailableReason").AsString());
        Assert.AreEqual("no-voice-for-language", engine.Evaluate("noVoice.unavailableReason").AsString());
    }

    private static Engine CreateEngine()
    {
        var engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(10)));
        engine.Execute(Harness);
        engine.Execute(File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "AniLingo.Web", "wwwroot", "js", "tts.js")));
        return engine;
    }

    private static double Number(Engine engine, string expression) =>
        engine.Evaluate(expression).AsNumber();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AniLingo.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AniLingo repository root.");
    }
}
