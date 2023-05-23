﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Build;

#nullable enable

namespace BuildXL.AdoBuildRunner.Vsts
{
    /// <summary>
    /// Defines the interactions with the VSTS API
    /// </summary>
    public interface IApi
    {
        /// <summary>
        /// VSTS BuildId. This is unique per VSTS account
        /// </summary>
        string BuildId { get; }

        /// <summary>
        /// Team project that the build definition belongs to
        /// </summary>
        string TeamProject { get; }

        /// <summary>
        /// The id of the team project the build definition belongs to
        /// </summary>
        string TeamProjectId { get; }

        /// <summary>
        /// Uri of the VSTS server that kicked off the build
        /// </summary>
        string ServerUri { get; }

        /// <summary>
        /// PAT token used to authenticate with VSTS
        /// </summary>
        string AccessToken { get; }

        /// <summary>
        /// Used to uniquely identify a VSTS Agent in each phase. Each Agent has consecutive number starting from 1.
        /// </summary>
        int JobPositionInPhase { get; }

        /// <summary>
        /// The total number of agents being requested to run the build in the given phase
        /// </summary>
        int TotalJobsInPhase { get; }

        /// <summary>
        /// Name of the Agent running the build
        /// </summary>
        string AgentName { get; }

        /// <summary>
        /// Folder where the sources are being built from
        /// </summary>
        string SourcesDirectory { get; }

        /// <summary>
        /// Id of the timeline of the build
        /// </summary>
        string TimelineId { get; }

        /// <summary>
        /// Id of the plan of the build
        /// </summary>
        string PlanId { get; }

        /// <summary>
        /// Url of the build repository
        /// </summary>
        string RepositoryUrl { get; }

        /// <summary>
        /// Get the address information of all the agents participating as workers
        /// </summary>
        /// <returns>Address information of all agents participating as workers, consisting of hostname and IP address entries</returns>
        Task<IEnumerable<IDictionary<string, string>>> GetWorkerAddressInformationAsync();

        /// <summary>
        /// Get the address information of the orchestrator
        /// </summary>
        /// <returns>Address information of the orchestrator, consisting of hostname and IP address entries</returns>
        Task<IEnumerable<IDictionary<string, string>>> GetOrchestratorAddressInformationAsync();

        /// <summary>
        /// Gets the build context from the ADO build run information
        /// </summary>
        Task<BuildContext> GetBuildContextAsync(string buildKey);

        /// <summary>
        /// Indicate that this machine is ready to build using a timeline record
        /// </summary>
        /// <returns></returns>
        Task SetMachineReadyToBuild(string hostName, string ipV4Address, string ipv6Address, bool isOrchestrator = false);

        /// <summary>
        /// Wait until all the other workers are ready
        /// </summary>
        /// <returns></returns>
        Task WaitForOtherWorkersToBeReady();

        /// <summary>
        /// Wait until the orchestrator is ready
        /// </summary>
        /// <returns></returns>
        Task WaitForOrchestratorToBeReady();

        /// <summary>
        /// Wait until the orchestrator is ready and return its address
        /// </summary>
        /// <returns></returns>
        Task<BuildInfo> WaitForBuildInfo(BuildContext buildContext);
       
        /// <summary>
        /// Publish the orchestrator address
        /// </summary>
        /// <returns></returns>
        Task PublishBuildInfo(BuildContext buildContext, BuildInfo buildInfo);

        /// <summary>
        /// Wait until the orchestrator is finished, and indicate success or failure of the build
        /// </summary>
        /// <returns>true if tue build succeeded in the orchestrator, false otherwise</returns>
        Task<bool> WaitForOrchestratorExit();

        /// <summary>
        /// Indicates the build result in this machine
        /// </summary>
        /// <returns></returns>
        Task SetBuildResult(bool success);

        /// <summary>
        /// Queue a build
        /// </summary>
        Task QueueBuildAsync(int pipelineId, string sourceBranch, string sourceVersion, Dictionary<string, string>? parameters = null, Dictionary<string, string>? templateParameters = null, Dictionary<string, string>? triggerInfo = null);
    }
}
