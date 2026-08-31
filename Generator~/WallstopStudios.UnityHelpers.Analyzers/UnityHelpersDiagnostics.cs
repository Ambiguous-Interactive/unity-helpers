// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// The <c>WUH###</c> family: diagnostics about code that already compiles and already works.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>WPROTO###</c> in both prefix and policy. A WallstopProto diagnostic reports
    /// a serialization contract that cannot be honoured, so it is an error -- the alternative is an
    /// exception from inside a shipped player. A <c>WUH###</c> diagnostic reports an allocation or a
    /// footgun in code that is otherwise correct, so it is capped at a warning: a consumer taking a
    /// package upgrade must never find their build failing over one. Every member of this family is
    /// on by default (a consumer should get the safety without discovering it) and suppressible.
    /// </remarks>
    internal static class UnityHelpersDiagnostics
    {
        /// <summary>
        /// A method group handed to a lookup's value factory allocates a delegate on every call,
        /// cache hit included, on every C# version Unity ships.
        /// </summary>
        /// <remarks>
        /// The shape is invisible without a semantic model: <c>GetOrAdd(key, Factory)</c> and
        /// <c>GetOrAdd(key, cachedFactory)</c> are the same token in argument position, so
        /// <c>scripts/lint-concurrent-cache-fill.ps1</c> -- which does enforce that every
        /// <b>lambda</b> handed to one of these is <c>static</c> -- cannot tell them apart. A casing
        /// heuristic would be wrong the first time a field is named <c>Factory</c> (#538).
        /// </remarks>
        internal static readonly DiagnosticDescriptor CacheFactoryAllocatesPerCall =
            new DiagnosticDescriptor(
                "WUH001",
                "Lookup factory method group allocates on every call",
                "'{0}' is passed to '{1}' as a method group, so a new delegate is built on every call -- including the calls that never invoke it, which is every lookup that already has the key. Measured at 106 bytes per call over 400,000 warm hits. Hold it in a 'static readonly' delegate field and pass that field, or use an overload that takes the state separately with a 'static' lambda.",
                "Performance",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        /// <summary>
        /// A Unity-serialized field that resolves onto a collection of collections, which Unity
        /// drops entirely and silently.
        /// </summary>
        /// <remarks>
        /// The declaration compiles, the Inspector renders it, and edits made there survive until
        /// the next reload -- so the failure presents as data that "does not save" rather than as a
        /// serialization error. The nesting is usually not visible at the declaration either: a
        /// <c>SerializableDictionary&lt;string, List&lt;Foo&gt;&gt;</c> reads as one collection and
        /// becomes <c>List&lt;Foo&gt;[]</c> only once its backing array is substituted (#548).
        /// </remarks>
        internal static readonly DiagnosticDescriptor NestedCollectionIsNotSerialized =
            new DiagnosticDescriptor(
                "WUH002",
                "Unity does not serialize a nested collection",
                "'{0}' resolves onto '{1}', a collection whose elements are themselves '{2}'. Unity serializes neither, and reports nothing: the asset keeps the outer structure and loses every inner value, while the Inspector goes on accepting edits that vanish on reload. Wrap the inner collection in a serializable class -- 'SerializableList<T>' ships for exactly this -- so the outer collection holds a class rather than another collection.",
                "Correctness",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        /// <summary>
        /// A null-conditional or null-coalescing operator applied to a <c>UnityEngine.Object</c>,
        /// which tests CLR null and so steps straight past a destroyed object.
        /// </summary>
        /// <remarks>
        /// <c>UnityEngine.Object</c> overloads <c>==</c> to report a destroyed object as null;
        /// <c>?.</c>, <c>??</c> and <c>??=</c> do not use that overload. So <c>obj?.Foo()</c> runs
        /// the member access on a destroyed object and <c>obj ?? fallback</c> hands the destroyed
        /// object back, both at exactly the moment the guard was written for. The signal is the
        /// receiver's type rather than the operator, which is why this cannot be a source linter:
        /// <c>Vector2? p; p?.x</c> is correct and common (#621).
        /// </remarks>
        internal static readonly DiagnosticDescriptor NullPropagationOnUnityObject =
            new DiagnosticDescriptor(
                "WUH003",
                "Null-propagation does not see a destroyed UnityEngine.Object",
                "'{0}' is a '{1}', and '{2}' compares against CLR null rather than through UnityEngine.Object's '==' overload -- so a destroyed object is treated as alive and the guard does nothing. Write the comparison out ('value != null ? value.Foo : fallback'), or test with 'Objects.NotNull'/'Objects.Null', which go through the overload.",
                "Correctness",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        /// <summary>
        /// An assertion that compares a <c>UnityEngine.Object</c> against CLR null, which passes
        /// over a destroyed object and so is green about a thing that is gone.
        /// </summary>
        /// <remarks>
        /// This one fails the opposite way from <see cref="NullPropagationOnUnityObject"/>: the
        /// fixture reports success. <c>NUnit.Framework.Assert.IsNotNull(destroyed)</c> passes, and
        /// <c>NUnit.Framework.Assert.IsNull(destroyed)</c> fails, because neither reaches the
        /// overload (#621).
        /// <para>
        /// <b>Scope is NUnit's <c>Assert</c> and nothing else.</b> Unity's own
        /// <c>UnityEngine.Assertions.Assert</c> is destroyed-aware and must NOT be reported:
        /// measured in a Unity 6000.4.6f1 editor on a destroyed <c>GameObject</c>, with
        /// <c>Assert.raiseExceptions = true</c> and an <c>IsNotNull((string)null)</c> control that
        /// did fail, <c>UnityEngine.Assertions.Assert.IsNull(destroyed)</c> PASSED and
        /// <c>IsNotNull(destroyed)</c> FAILED -- both destroyed-aware answers, and both the
        /// opposite of what they answer for a live object. Its <c>IsNull&lt;T&gt;</c> /
        /// <c>IsNotNull&lt;T&gt;</c> forward to a <c>UnityEngine.Object</c> overload that compares
        /// through the <c>==</c> operator. Covering that namespace was a false positive on correct
        /// code; do not restore it.
        /// </para>
        /// </remarks>
        internal static readonly DiagnosticDescriptor NullAssertionOnUnityObject =
            new DiagnosticDescriptor(
                "WUH004",
                "A null assertion passes over a destroyed UnityEngine.Object",
                "'{0}' compares '{1}' against CLR null, which a destroyed UnityEngine.Object does not equal -- so this assertion passes over an object that is gone. Assert through the overload instead: 'Assert.IsTrue({2} != null)' for present, 'Assert.IsTrue({2} == null)' for destroyed-or-absent.",
                "Correctness",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        /// <summary>
        /// A use of <c>UnityEngine.Random</c>, whose state no test can set or read without
        /// disturbing every other caller.
        /// </summary>
        /// <remarks>
        /// The package ships ~20 seedable, serializable generators behind <c>IRandom</c>. Anything
        /// built on one can be re-run and asserted; the same code on <c>UnityEngine.Random</c>
        /// produces a bug report with no way to reproduce it, and swapping afterwards changes every
        /// call site at once. <c>System.Random</c> is a different mistake and is out of scope
        /// (#622).
        /// <para>
        /// Naming the nested <c>State</c> type in declaration position stays under THIS id rather
        /// than a second one. A type annotation draws nothing, so the message says "ties this code
        /// to" rather than "reads" and names <c>RandomState</c> beside <c>PRNG.Instance</c>; a
        /// separate id would instead have walked out from under every <c>#pragma warning disable
        /// WUH005</c> a consumer had already written around a deliberate save/restore, which is a
        /// package upgrade re-raising a warning they had answered.
        /// </para>
        /// </remarks>
        internal static readonly DiagnosticDescriptor UnityRandomIsNotReplayable =
            new DiagnosticDescriptor(
                "WUH005",
                "UnityEngine.Random cannot be replayed without moving every other caller",
                "'UnityEngine.Random.{0}' ties this code to the engine's one process-global generator -- a member draws from it, and its 'State' snapshot resumes only into it. That generator can be set and read ('InitState' and 'state' are exactly those two members), but only by moving every other caller along with it, so a test cannot replay one system's draws in isolation. Use 'PRNG.Instance', or take an 'IRandom' field a test can seed, and hold 'RandomState' -- which every 'IRandom' hands out through 'InternalState' -- rather than 'UnityEngine.Random.State'. Porting a range needs care: 'UnityEngine.Random.Range(x, x)' returns x, while 'IRandom.NextFloat(min, max)' throws when 'max' is not greater than 'min', so a spread that may legitimately be zero wants 'NextFloatInRange'.",
                "Correctness",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        /// <summary>
        /// A discarded <c>EffectHandle</c>, which is the only thing that can remove an
        /// infinite-duration effect.
        /// </summary>
        /// <remarks>
        /// Duration is authored data the compiler cannot see, and an effect can be re-authored to
        /// <c>Infinite</c> long after the call site was written -- which is how the failure arrives.
        /// So the diagnostic is deliberately not gated on the duration type.
        /// <c>ForceApplyEffect</c> is the deliberate no-handle overload and is out of scope (#623).
        /// </remarks>
        internal static readonly DiagnosticDescriptor DiscardedEffectHandle =
            new DiagnosticDescriptor(
                "WUH006",
                "A discarded EffectHandle cannot remove the effect it applied",
                "'{0}' returns the handle that removes the effect, and this call drops it. An infinite-duration effect expires from nothing else, and the object carrying it routinely outlives whatever applied it. Store the handle somewhere that outlives the effect and remove through it, or call 'ForceApplyEffect' if this effect is instant and nothing will ever need to take it off.",
                "Correctness",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        /// <summary>
        /// A discarded coroutine handle, which is the only thing that can stop the work or answer
        /// whether it is already running.
        /// </summary>
        /// <remarks>
        /// Matching <c>StartCoroutine</c> alone is not enough: in the tree this was measured on, the
        /// package's own periodic-job and delay helpers each outnumbered raw <c>StartCoroutine</c>,
        /// so a name-only rule saw 9 of 44 call sites. Reassignment over a live handle (shape 2 on
        /// the issue) is a dataflow question and is deliberately out of scope here (#626).
        /// </remarks>
        internal static readonly DiagnosticDescriptor DiscardedCoroutineHandle =
            new DiagnosticDescriptor(
                "WUH007",
                "A discarded coroutine handle cannot stop the coroutine",
                "'{0}' returns the only handle that can stop this work or answer whether it is already running, and this call drops it. 'StopAllCoroutines' is then the sole remaining lever, and it also stops whatever is doing the stopping. Store the handle in a field the owner clears where its state ends -- a 'List<Coroutine>' where one owner starts many -- or suppress with a reason if this work must outlive its starter.",
                "Correctness",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        /// <summary>
        /// A read of a <c>TryXxx</c> <c>out</c> value on a path where the call's result was never
        /// tested.
        /// </summary>
        /// <remarks>
        /// The compiler already forces the callee to assign every <c>out</c> on every return path,
        /// so the hazard is not an unwritten slot -- it is an UNSPECIFIED one. The BCL happens to
        /// write <c>default</c> on a miss; nothing obliges anyone else's <c>TryXxx</c> to, and this
        /// package ships plenty of them, so the same shape over its own API reads whatever the
        /// callee left there. A <c>default</c> struct or a <c>0</c> count is a plausible value, so
        /// the symptom is wrong behaviour rather than a crash (#629).
        /// </remarks>
        internal static readonly DiagnosticDescriptor UntestedTryOutValueIsRead =
            new DiagnosticDescriptor(
                "WUH008",
                "A TryXxx out value is read without testing the call",
                "'{0}' returns whether it wrote '{1}', and this code reads '{1}' without testing that result. Definite assignment obliges the callee to write every 'out' before it returns, so the value is unspecified rather than unwritten: on the failing path this reads whatever the callee left there. Many APIs write 'default'; nothing in the contract promises it. Guard the read: 'if (!{0}(..., out var {1})) {{ return; }}'.",
                "Correctness",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        /// <summary>
        /// A teardown override whose <c>base</c> call runs before body statements that still need
        /// what the base takes away.
        /// </summary>
        /// <remarks>
        /// Setup chains base-FIRST and teardown chains base-LAST, which is why "always call base
        /// first" is wrong advice and why the mistake is natural: base-first is correct everywhere
        /// else in the same file. The base call is where a <c>RuntimeSingleton</c> drops its
        /// registration, so anything after it runs against a half-dismantled object (#630).
        /// </remarks>
        internal static readonly DiagnosticDescriptor TeardownBaseCallIsNotLast =
            new DiagnosticDescriptor(
                "WUH009",
                "A teardown's base call runs before the body that still needs it",
                "'{0}' releases what this object registered -- a singleton registration, a messaging token -- and {1} statement(s) run after it, against an object that is already half dismantled. Teardown chains base-LAST: move the '{0}' call to the end of the body. (Setup is the opposite: 'Awake' and 'OnEnable' must chain base-first.)",
                "Correctness",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );
    }
}
