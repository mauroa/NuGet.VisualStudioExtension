﻿using Microsoft.VisualStudio.ProjectSystem.Interop;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.ProjectManagement;
using NuGet.ProjectManagement.Projects;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.PackageManagement.VisualStudio
{
    public class NuGetPackageMoniker : INuGetPackageMoniker
    {
        public string Id
        {
            get;
            set;
        }

        public string Version
        {
            get;
            set;
        }
    }

    public class ProjectKNuGetProject : ProjectKNuGetProjectBase
    {
        private INuGetPackageManager _project;

        public ProjectKNuGetProject(INuGetPackageManager project, string projectName)
        {
            _project = project;
            InternalMetadata.Add(NuGetProjectMetadataKeys.Name, projectName);
        }

        private static bool IsCompatible(
            NuGetFramework projectFrameworkName,
            IEnumerable<NuGetFramework> packageSupportedFrameworks)
        {
            if (packageSupportedFrameworks.Any())
            {
                return packageSupportedFrameworks.Any(packageSupportedFramework =>
                    NuGet.Frameworks.DefaultCompatibilityProvider.Instance.IsCompatible(
                        projectFrameworkName,
                        packageSupportedFramework));
            }

            // No supported frameworks means that everything is supported.
            return true;
        }

        public async override Task<bool> InstallPackageAsync(PackagingCore.PackageIdentity packageIdentity, System.IO.Stream packageStream,
            INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            if (!packageStream.CanSeek)
            {
                throw new ArgumentException(NuGet.ProjectManagement.Strings.PackageStreamShouldBeSeekable);
            }

            packageStream.Seek(0, SeekOrigin.Begin);
            var zipArchive = new ZipArchive(packageStream);
            PackageReader packageReader = new PackageReader(zipArchive);
            var packageSupportedFrameworks = packageReader.GetSupportedFrameworks();
            var projectFrameworks = _project.GetSupportedFrameworksAsync(token)
                .Result
                .Select(f => NuGetFramework.Parse(f.FullName));

            var args = new Dictionary<string, object>();
            args["Frameworks"] = projectFrameworks.Where(
                projectFramework =>
                    IsCompatible(projectFramework, packageSupportedFrameworks)).ToArray();
            await _project.InstallPackageAsync(
                new NuGetPackageMoniker
                {
                    Id = packageIdentity.Id,
                    Version = packageIdentity.Version.ToNormalizedString()
                },
                args,
                logger: null,
                progress: null,
                cancellationToken: token);
            return true;
        }

        public async override Task<bool> UninstallPackageAsync(PackagingCore.PackageIdentity packageIdentity, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            var args = new Dictionary<string, object>();
            await _project.UninstallPackageAsync(
                new NuGetPackageMoniker
                {
                    Id = packageIdentity.Id,
                    Version = packageIdentity.Version.ToNormalizedString()
                },
                args,
                logger: null,
                progress: null,
                cancellationToken: token);
            return true;
        }

        public async override Task<IEnumerable<Packaging.PackageReference>> GetInstalledPackagesAsync(CancellationToken token)
        {
            var result = new List<Packaging.PackageReference>();
            foreach (object item in await _project.GetInstalledPackagesAsync(token))
            {
                PackagingCore.PackageIdentity identity = null;

                var moniker = item as INuGetPackageMoniker;
                if (moniker != null)
                {
                    identity = new PackagingCore.PackageIdentity(
                        moniker.Id,
                        NuGetVersion.Parse(moniker.Version));
                }
                else
                {
                    // otherwise, item is the file name of the nupkg file
                    var fileName = item as string;
                    using (var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                    {
                        var zipArchive = new ZipArchive(fileStream);
                        var packageReader = new PackageReader(zipArchive);
                        identity = packageReader.GetIdentity();
                    }
                }

                result.Add(new Packaging.PackageReference(
                        identity,
                        targetFramework: null));
            }

            return result;
        }
    }
}