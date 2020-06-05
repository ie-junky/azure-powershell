﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using Microsoft.Azure.Management.ResourceManager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace Microsoft.Azure.Commands.ResourceManager.Cmdlets.SdkModels
{
    /// <summary>
    /// Represents a Template Spec Version within a Template Spec.
    /// </summary>
    public class PSTemplateSpecVersion
    {
        /// <summary>
        /// Gets or sets the name of the version. For example: 'v1'
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the description of the version.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets tags assigned to the version.
        /// </summary>
        public IDictionary<string, string> Tags { get; set; }

        /// <summary>
        /// Gets or sets the artifacts within the template spec version
        /// </summary>
        public IList<PSTemplateSpecArtifact> Artifacts { get; set; } = 
            new List<PSTemplateSpecArtifact>();

        /// <summary>
        /// Gets or sets the Azure Resource Manager template.
        /// </summary>
        public object Template { get; set; }

        /// <summary>
        /// Gets the date/time the template spec version was created (PUT to Azure).
        /// </summary>
        public DateTime? CreationTime { get; private set; }

        /// <summary>
        /// Gets the last date/time the template spec version was modified (PUT to Azure).
        /// </summary>
        public DateTime? LastModifiedTime { get; private set; }

        /// <summary>
        /// Converts a template spec model from the Azure SDK to the powershell
        /// exposed template spec model.
        /// </summary>
        /// <param name="templateSpecVersion">The Azure SDK template spec model</param>
        /// <returns>The converted model or null if no model was specified</returns>
        internal static PSTemplateSpecVersion FromAzureSDKTemplateSpecVersion(
            TemplateSpecVersionModel templateSpecVersion)
        {
            if (templateSpecVersion == null)
            {
                return null;
            }

            var psTemplateSpecVersion = new PSTemplateSpecVersion
            {
                CreationTime = templateSpecVersion.SystemData.CreatedAt,
                LastModifiedTime = templateSpecVersion.SystemData.LastModifiedAt,
                Name = templateSpecVersion.Name,
                Description = templateSpecVersion.Description,
                Tags = templateSpecVersion.Tags,
                Template = templateSpecVersion.Template
            };

            if (templateSpecVersion.Artifacts?.Any() == true) {
                foreach (TemplateSpecArtifact artifact in templateSpecVersion.Artifacts)
                {
                    switch (artifact)
                    {
                        case TemplateSpecTemplateArtifact templateArtifact:
                            psTemplateSpecVersion.Artifacts.Add(
                                PSTemplateSpecTemplateArtifact.FromAzureSDKTemplateSpecTemplateArtifact(templateArtifact)
                            );
                            break;
                        default:
                            // TODO: Localize
                            throw new PSNotSupportedException(
                                $"Template spec artifact type '${artifact.GetType().Name}' not supported by cmdlets."
                            );
                    }
                }
            }

            return psTemplateSpecVersion;
        }
    }
}