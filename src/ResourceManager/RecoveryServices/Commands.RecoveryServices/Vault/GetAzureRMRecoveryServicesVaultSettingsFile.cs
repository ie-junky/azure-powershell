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

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using Microsoft.Azure.Commands.Common.Authentication;
using Microsoft.Azure.Commands.Common.Authentication.Abstractions;
using Microsoft.Azure.Commands.RecoveryServices.Properties;
using Microsoft.Azure.Management.RecoveryServices.Models;
using Microsoft.Azure.Portal.RecoveryServices.Models.Common;

namespace Microsoft.Azure.Commands.RecoveryServices
{
    /// <summary>
    /// Retrieves Azure Recovery Services Vault Settings File.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "AzureRmRecoveryServicesVaultSettingsFile")]
    [OutputType(typeof(VaultSettingsFilePath))]
    public class GetAzureRmRecoveryServicesVaultSettingsFile : RecoveryServicesCmdletBase
    {
        /// <summary>
        /// Expiry in hours for generated certificate.
        /// </summary>
        private const int VaultCertificateExpiryInHoursForHRM = 120;

        /// <summary>
        /// Expiry in hours for generated certificate.
        /// </summary>
        private const int VaultCertificateExpiryInHoursForBackup = 48;

        /// <summary>
        /// Vault Credential version.
        /// </summary>
        public const string VaultCredentialVersionAad = "2.0";

        /// <summary>
        /// Recovery services vault type.
        /// </summary>
        public const string RecoveryServicesVaultType = "Vaults";

        #region Parameters

        /// <summary>
        /// Gets or sets vault Object.
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 1)]
        [ValidateNotNullOrEmpty]
        public ARSVault Vault { get; set; }

        /// <summary>
        /// Gets or sets Site Identifier.
        /// </summary>
        [Parameter(ParameterSetName = ARSParameterSets.ForSite, Mandatory = true)]
        [ValidateNotNullOrEmpty]
        public String SiteIdentifier { get; set; }

        /// <summary>
        /// Gets or sets SiteFriendlyName.
        /// </summary>
        [Parameter(ParameterSetName = ARSParameterSets.ForSite, Mandatory = true)]
        [ValidateNotNullOrEmpty]
        public String SiteFriendlyName { get; set; }

        /// <summary>
        /// Gets or sets the path where the credential file is to be generated
        /// </summary>
        /// <summary>
        /// Gets or sets vault Object.
        /// </summary>
        [Parameter(Position = 2)]
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets the path where the credential file is to be generated
        /// </summary>
        /// <summary>
        /// Gets or sets vault Object.
        /// </summary>
        [Parameter(ParameterSetName = ARSParameterSets.ByDefault, Mandatory = false)]
        [Parameter(ParameterSetName = ARSParameterSets.ForSite, Mandatory = false)]
        public SwitchParameter SiteRecovery
        {
            get { return siteRecovery; }
            set { siteRecovery = value; }
        }
        private bool siteRecovery;

        /// <summary>
        /// Gets or sets the path where the credential file is to be generated
        /// </summary>
        /// <summary>
        /// Gets or sets vault Object.
        /// </summary>
        [Parameter(ParameterSetName = ARSParameterSets.ForBackupVaultType, Mandatory = true)]
        public SwitchParameter Backup
        {
            get { return backup; }
            set { backup = value; }
        }
        private bool backup;

        #endregion Parameters

        /// <summary>
        /// ProcessRecord of the command.
        /// </summary>
        public override void ExecuteCmdlet()
        {
            try
            {
                if (backup)
                {
                    this.GetAzureRMRecoveryServicesVaultBackupCredentials();
                }
                else
                {
                    this.GetSiteRecoveryCredentials();
                }
            }
            catch (AggregateException aggregateEx)
            {
                // if an exception is thrown from a task, it will be wrapped in AggregateException 
                // and propagated to the main thread. Just throwing the first exception in the list.
                Exception exception = aggregateEx.InnerExceptions.First<Exception>();
                this.HandleException(exception);
            }
        }

        /// <summary>
        /// Method to execute the command
        /// </summary>
        private void GetSiteRecoveryCredentials()
        {
            IAzureSubscription subscription = DefaultProfile.DefaultContext.Subscription;

            // Generate certificate
            X509Certificate2 cert = CertUtils.CreateSelfSignedCertificate(
                VaultCertificateExpiryInHoursForHRM,
                subscription.Id.ToString(),
                this.Vault.Name);

            ASRSite site = new ASRSite();

            if (!string.IsNullOrEmpty(this.SiteIdentifier)
                   && !string.IsNullOrEmpty(this.SiteFriendlyName))
            {
                site.ID = this.SiteIdentifier;
                site.Name = this.SiteFriendlyName;
            }

            string fileName = this.GenerateFileName();

            string filePath = string.IsNullOrEmpty(this.Path) ? Utilities.GetDefaultPath() : this.Path;

            // Generate file.
            if (RecoveryServicesClient.getVaultAuthType(this.Vault.ResourceGroupName, this.Vault.Name) == 0)
            {
                ASRVaultCreds vaultCreds = RecoveryServicesClient.GenerateVaultCredential(
                    cert,
                    this.Vault,
                    site,
                    AuthType.ACS);

                // write the content to a file.
                VaultSettingsFilePath output = new VaultSettingsFilePath()
                {
                    FilePath = Utilities.WriteToFile<ASRVaultCreds>(vaultCreds, filePath, fileName)
                };

                // print the path to the user.
                this.WriteObject(output, true);
            }
            else
            {
                string fullFilePath = System.IO.Path.Combine(filePath, fileName);
                WriteDebug(
                     string.Format(
                      CultureInfo.InvariantCulture,
                      Resources.ExecutingGetVaultCredCmdlet,
                      subscription.Id,
                      this.Vault.ResourceGroupName,
                      this.Vault.Name,
                      fullFilePath));

                VaultCertificateResponse vaultCertificateResponse = null;
                try
                {
                    // Upload cert into ID Mgmt
                    WriteDebug(string.Format(CultureInfo.InvariantCulture, Resources.UploadingCertToIdmgmt));
                    vaultCertificateResponse = UploadCert(cert);
                    WriteDebug(string.Format(CultureInfo.InvariantCulture, Resources.UploadedCertToIdmgmt));

                    // generate vault credentials
                    string vaultCredsFileContent = GenerateVaultCredsForSiteRecovery(
                        cert,
                        subscription.Id,
                        vaultCertificateResponse,
                        site);

                    WriteDebug(string.Format(Resources.SavingVaultCred, fullFilePath));

                    AzureSession.Instance.DataStore.WriteFile(fullFilePath, Encoding.UTF8.GetBytes(vaultCredsFileContent));

                    VaultSettingsFilePath output = new VaultSettingsFilePath()
                    {
                        FilePath = fullFilePath,
                    };

                    // Output filename back to user
                    WriteObject(output, true);
                }
                catch (Exception exception)
                {
                    throw exception;
                }
            }
        }

        /// <summary>
        /// Method to generate the file name
        /// </summary>
        /// <returns>file name as string.</returns>
        private string GenerateFileName()
        {
            string fileName;
            string format = "yyyy-MM-ddTHH-mm-ss";

            if (string.IsNullOrEmpty(this.SiteIdentifier) || string.IsNullOrEmpty(this.SiteFriendlyName))
            {
                fileName = string.Format("{0}_{1}.VaultCredentials", this.Vault.Name, DateTime.UtcNow.ToString(format));
            }
            else
            {
                fileName = string.Format("{0}_{1}_{2}.VaultCredentials", this.SiteFriendlyName, this.Vault.Name, DateTime.UtcNow.ToString(format));
            }

            return fileName;
        }

        #region Backup Vault Credentials
        /// <summary>
        /// Get vault credentials for backup vault type.
        /// </summary>
        public void GetAzureRMRecoveryServicesVaultBackupCredentials()
        {
            string targetLocation = string.IsNullOrEmpty(this.Path) ? Utilities.GetDefaultPath() : this.Path;
            if (!Directory.Exists(targetLocation))
            {
                throw new ArgumentException(Resources.VaultCredPathException);
            }

            string subscriptionId = DefaultContext.Subscription.Id.ToString();
            string displayName = this.Vault.Name;

            WriteDebug(string.Format(CultureInfo.InvariantCulture,
                                      Resources.ExecutingGetVaultCredCmdlet,
                                      subscriptionId, this.Vault.ResourceGroupName, this.Vault.Name, targetLocation));

            // Generate certificate
            X509Certificate2 cert = CertUtils.CreateSelfSignedCertificate(
                VaultCertificateExpiryInHoursForBackup, subscriptionId.ToString(), this.Vault.Name);

            VaultCertificateResponse vaultCertificateResponse = null;
            string channelIntegrityKey = string.Empty;
            try
            {
                // Upload cert into ID Mgmt
                WriteDebug(string.Format(CultureInfo.InvariantCulture, Resources.UploadingCertToIdmgmt));
                vaultCertificateResponse = UploadCert(cert);
                WriteDebug(string.Format(CultureInfo.InvariantCulture, Resources.UploadedCertToIdmgmt));
            }
            catch (Exception exception)
            {
                throw exception;
            }

            // generate vault credentials
            string vaultCredsFileContent = GenerateVaultCreds(cert, subscriptionId, vaultCertificateResponse);

            // NOTE: One of the scenarios for this cmdlet is to generate a file which will be an input 
            //       to DPM servers. 
            //       We found a bug in the DPM UI which is looking for a particular namespace in the input file.
            //       The below is a hack to circumvent this issue and this would be removed once the bug can be fixed.
            vaultCredsFileContent = vaultCredsFileContent.Replace("Microsoft.Azure.Commands.AzureBackup.Models",
                "Microsoft.Azure.Portal.RecoveryServices.Models.Common");

            // prepare for download
            string fileName = string.Format("{0}_{1:ddd MMM dd yyyy}.VaultCredentials", displayName, DateTime.UtcNow);
            string filePath = System.IO.Path.Combine(targetLocation, fileName);
            WriteDebug(string.Format(Resources.SavingVaultCred, filePath));

            AzureSession.Instance.DataStore.WriteFile(filePath, Encoding.UTF8.GetBytes(vaultCredsFileContent));

            VaultSettingsFilePath output = new VaultSettingsFilePath()
            {
                FilePath = filePath,
            };

            // Output filename back to user
            WriteObject(output);
        }

        /// <summary>
        /// Upload certificate
        /// </summary>
        /// <param name="cert">management certificate</param>
        /// <returns>acs namespace of the uploaded cert</returns>
        private VaultCertificateResponse UploadCert(X509Certificate2 cert)
        {
            return RecoveryServicesClient.UploadCertificate(cert, this.Vault);
        }

        /// <summary>
        /// Generates vault creds file
        /// </summary>
        /// <param name="cert">management certificate</param>
        /// <param name="subscriptionId">subscription Id</param>
        /// <param name="acsNamespace">acs namespace</param>
        /// <returns>xml file in string format</returns>
        private string GenerateVaultCreds(X509Certificate2 cert, string subscriptionId, VaultCertificateResponse vaultCertificateResponse)
        {
            try
            {
                return GenerateVaultCredsForBackup(cert, subscriptionId, vaultCertificateResponse);
            }
            catch (Exception exception)
            {
                throw exception;
            }
        }

        /// <summary>
        /// Generates vault creds file content for backup Vault
        /// </summary>
        /// <param name="cert">management certificate</param>
        /// <param name="subscriptionId">subscription Id</param>
        /// <param name="acsNamespace">acs namespace</param>
        /// <returns>xml file in string format</returns>
        private string GenerateVaultCredsForBackup(X509Certificate2 cert, string subscriptionId,
            VaultCertificateResponse vaultCertificateResponse)
        {
            using (var output = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(output, GetXmlWriterSettings()))
                {
                    ResourceCertificateAndAadDetails aadDetails = vaultCertificateResponse.Properties as ResourceCertificateAndAadDetails;

                    RSBackupVaultAADCreds vaultCreds = new RSBackupVaultAADCreds()
                    {
                        SubscriptionId = subscriptionId,
                        ResourceName = Vault.Name,
                        ManagementCert = CertUtils.SerializeCert(cert, X509ContentType.Pfx),
                        ResourceId = aadDetails.ResourceId.Value,
                        AadAuthority = aadDetails.AadAuthority,
                        AadTenantId = aadDetails.AadTenantId,
                        ServicePrincipalClientId = aadDetails.ServicePrincipalClientId,
                        IdMgmtRestEndpoint = aadDetails.AzureManagementEndpointAudience,
                        ProviderNamespace = PSRecoveryServicesClient.ProductionRpNamespace,
                        ResourceGroup = Vault.ResourceGroupName,
                        Location = Vault.Location,
                        Version = VaultCredentialVersionAad,
                        ResourceType = RecoveryServicesVaultType,
                        AgentLinks = GetAgentLinks()
                    };

                    DataContractSerializer serializer = new DataContractSerializer(typeof(RSBackupVaultAADCreds));
                    serializer.WriteObject(writer, vaultCreds);

                    WriteDebug(string.Format(CultureInfo.InvariantCulture, Resources.BackupVaultSerialized));
                }

                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        /// <summary>
        /// Generates vault creds file content for Site Recovery Vault
        /// </summary>
        /// <param name="cert">management certificate</param>
        /// <param name="subscriptionId">subscription Id</param>
        /// <param name="vaultCertificateResponse">vaultCertificate Response</param>
        /// <param name="asrSite">asrSite Info</param>
        /// <returns>xml file in string format</returns>
        private string GenerateVaultCredsForSiteRecovery(X509Certificate2 cert, string subscriptionId,
            VaultCertificateResponse vaultCertificateResponse, ASRSite asrSite)
        {
            using (var output = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(output, GetXmlWriterSettings()))
                {
                    ResourceCertificateAndAadDetails aadDetails = vaultCertificateResponse.Properties as ResourceCertificateAndAadDetails;
                    string resourceProviderNamespace = string.Empty;
                    string resourceType = string.Empty;

                    Utilities.GetResourceProviderNamespaceAndType(this.Vault.ID, out resourceProviderNamespace, out resourceType);

                    Logger.Instance.WriteDebug(string.Format(
                        "GenerateVaultCredential resourceProviderNamespace = {0}, resourceType = {1}",
                        resourceProviderNamespace,
                        resourceType));

                    // Update vault settings with the working vault to generate file
                    Utilities.UpdateCurrentVaultContext(new ASRVaultCreds()
                    {
                        ResourceGroupName = this.Vault.ResourceGroupName,
                        ResourceName = this.Vault.Name,
                        ResourceNamespace = resourceProviderNamespace,
                        ARMResourceType = resourceType
                    });

                    //Code taken from Ibiza code
                    string aadAudience = string.Format(CultureInfo.InvariantCulture,
                        @"https://RecoveryServiceVault/{0}/{1}/{2}",
                        Vault.Location,
                        Vault.Name,
                        aadDetails.ResourceId);

                    RSVaultAsrCreds vaultCreds = new RSVaultAsrCreds()
                    {
                        VaultDetails = new ASRVaultDetails
                        {
                            SubscriptionId = subscriptionId,
                            ResourceGroup = this.Vault.ResourceGroupName,
                            ResourceName = this.Vault.Name,
                            ResourceId = aadDetails.ResourceId.Value,
                            Location = Vault.Location,
                            ResourceType = RecoveryServicesVaultType,
                            ProviderNamespace = PSRecoveryServicesClient.ProductionRpNamespace
                        },
                        ManagementCert = CertUtils.SerializeCert(cert, X509ContentType.Pfx),
                        Version = VaultCredentialVersionAad,
                        AadDetails = new ASRVaultAadDetails
                        {
                            AadAuthority = aadDetails.AadAuthority,
                            AadTenantId = aadDetails.AadTenantId,
                            ServicePrincipalClientId = aadDetails.ServicePrincipalClientId,
                            AadVaultAudience = aadAudience,
                            ArmManagementEndpoint = aadDetails.AzureManagementEndpointAudience
                        },
                        ChannelIntegrityKey = this.RecoveryServicesClient.GetCurrentVaultChannelIntegrityKey(),
                        SiteId = asrSite.ID == null ? String.Empty : asrSite.ID,
                        SiteName = asrSite.Name == null ? String.Empty : asrSite.Name
                    };

                    DataContractSerializer serializer = new DataContractSerializer(typeof(RSVaultAsrCreds));
                    serializer.WriteObject(writer, vaultCreds);
                }

                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        /// <summary>
        /// Get Agent Links
        /// </summary>
        /// <returns>Agent links in string format</returns>
        private static string GetAgentLinks()
        {
            return "WABUpdateKBLink,http://go.microsoft.com/fwlink/p/?LinkId=229525;" +
                   "StorageQuotaPurchaseLink,http://go.microsoft.com/fwlink/?LinkId=205490;" +
                   "WebPortalLink,http://go.microsoft.com/fwlink/?LinkId=817353;" +
                   "WABprivacyStatement,http://go.microsoft.com/fwlink/?LinkId=221308";
        }

        /// <summary>
        /// A set of XmlWriterSettings to use for the publishing profile
        /// </summary>
        /// <returns>The XmlWriterSettings set</returns>
        private XmlWriterSettings GetXmlWriterSettings()
        {
            return new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineOnAttributes = true
            };
        }

        #endregion
    }
}
