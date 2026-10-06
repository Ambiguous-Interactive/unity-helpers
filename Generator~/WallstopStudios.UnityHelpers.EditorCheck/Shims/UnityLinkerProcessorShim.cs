// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace UnityEditor.Build
{
    using UnityEditor.Build.Reporting;
    using UnityEditor.UnityLinker;

    /// <summary>
    /// Matches Unity 2021.3's interface after removal of the 2021.1 before/after callbacks.
    /// </summary>
    /// <remarks>
    /// https://github.com/Unity-Technologies/UnityCsReference/blob/2021.3/Editor/Mono/BuildPipeline/BuildPipelineInterfaces.cs
    /// Native Unity tests remain the proof of callback discovery and execution.
    /// </remarks>
    public interface IUnityLinkerProcessor : IOrderedCallback
    {
        /// <summary>
        /// Supplies an additional descriptor path to the Unity linker.
        /// </summary>
        string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data);
    }
}
