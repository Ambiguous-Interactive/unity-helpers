// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;

    /// <summary>
    /// Guards against an editor crash: building a <see cref="GUIStyle"/> in a static field
    /// initializer or static constructor. Those run when the type is first loaded, which can happen
    /// OUTSIDE an active IMGUI pass (e.g. batch-mode test runs). A style that touches the editor
    /// skin (<c>EditorStyles.*</c>, <c>GUI.skin</c>, <c>new GUIStyle("Button")</c>) throws a
    /// <see cref="System.NullReferenceException"/> at type-load, surfacing as a
    /// <see cref="System.TypeInitializationException"/> that cascades into every test touching the
    /// drawer (this once turned a single drawer bug into 830 failing tests).
    ///
    /// The robust pattern this codebase uses instead is lazy initialization: a non-readonly backing
    /// field assigned via <c>??=</c> inside a property, so the style is built on first GUI access.
    ///
    /// Scope: this is a metadata-only coding-standard guard. It flags the idiomatic form of the bug
    /// -- a <c>static readonly GUIStyle</c> field, which is init-only and can therefore only be
    /// assigned eagerly at type-load. It deliberately does NOT read field values or force any static
    /// constructor (that would be order-dependent and would falsely pass once the skin is available),
    /// which is what keeps it deterministic and independent of test execution order. It does not
    /// attempt to catch a non-readonly eager <c>static GUIStyle = new GUIStyle("...")</c> (not
    /// expressible in metadata); the lazy-property convention this enforces makes that form a code
    /// smell that review catches.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EditorDrawerStaticStyleInitializationTests
    {
        [Test]
        public void NoEditorTypeEagerlyInitializesStaticGuiStyle()
        {
            // Anchor on a known editor drawer to resolve the shipped Editor assembly, then scan
            // every type (including nested ones) it contains. Tolerate partially-loadable assemblies
            // (e.g. a future reference to an optional package whose types fail to load) by inspecting
            // whatever types did resolve rather than throwing a raw ReflectionTypeLoadException.
            Assembly editorAssembly = typeof(SerializableSetPropertyDrawer).Assembly;

            System.Type[] types;
            try
            {
                types = editorAssembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }

            List<string> offenders = new();
            foreach (System.Type type in types)
            {
                FieldInfo[] fields = type.GetFields(
                    BindingFlags.Static
                        | BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly
                );
                foreach (FieldInfo field in fields)
                {
                    if (field.FieldType == typeof(GUIStyle) && field.IsInitOnly)
                    {
                        offenders.Add($"{type.FullName}.{field.Name}");
                    }
                }
            }

            Assert.That(
                offenders,
                Is.Empty,
                "Found static readonly GUIStyle field(s) that are eagerly built at type-load. "
                    + "Convert each to a lazily-initialized property (non-readonly backing field "
                    + "assigned via '??=') so the editor skin is only touched during an active GUI "
                    + "pass:\n  "
                    + string.Join("\n  ", offenders.OrderBy(o => o))
            );
        }
    }
}
