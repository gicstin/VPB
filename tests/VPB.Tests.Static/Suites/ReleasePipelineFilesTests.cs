using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests.Static
{
    public class ReleasePipelineFilesTests
    {
        private readonly ITestOutputHelper _out;
        public ReleasePipelineFilesTests(ITestOutputHelper output) { _out = output; }

        // Paths the build, the hooks and CI invoke by name. A CI checkout contains only tracked
        // files, so anything missing here is a file that exists on someone's disk but never
        // reached the repository - which is easy to do because scripts/ is broadly gitignored.
        private static readonly string[] Required =
        {
            "scripts/BuildPatchManifest.ps1",
            "scripts/PublishRelease.ps1",
            "scripts/CheckReleaseIndexFresh.ps1",
            "scripts/PostBuildDeploy.ps1",
            "scripts/PreparePluginVersion.ps1",
            "tests/hooks/pre-commit",
            "tests/hooks/pre-push",
            "tests/hooks/post-commit",
            "releases/index.json",
            "vam_patch/patch_manifest.json",
            "vam_patch/patch_manifest2.json",
        };

        [Fact]
        public void EveryFileTheReleasePipelineInvokesIsInTheRepository()
        {
            var missing = Required
                .Where(p => !File.Exists(Repo.Path_(p.Replace('/', Path.DirectorySeparatorChar))))
                .ToList();

            _out.WriteLine("pipeline files checked: " + Required.Length);

            Assert.True(missing.Count == 0,
                "Files the release pipeline runs by name are not in the repository:" + Environment.NewLine +
                Repo.Bullets(missing) + Environment.NewLine +
                "They may exist on your disk while being untracked - scripts/ is gitignored except for named" +
                Environment.NewLine +
                "re-includes, so git status will not mention them. Check .gitignore, then git add the file." +
                Environment.NewLine +
                "Left unfixed, CI fails later with an unhelpful 'is not recognized as the name of a script file'.");
        }

        [Fact]
        public void WorkflowsOnlyInvokeScriptsThatExist()
        {
            string workflowDir = Repo.Path_(".github", "workflows");
            if (!Directory.Exists(workflowDir)) return;

            var broken = new List<string>();
            foreach (string file in Directory.GetFiles(workflowDir, "*.yml", SearchOption.AllDirectories))
            {
                foreach (string line in File.ReadAllLines(file))
                {
                    int at = line.IndexOf("-File ", StringComparison.Ordinal);
                    if (at < 0) continue;

                    string rest = line.Substring(at + "-File ".Length).Trim().Trim('"', '\'');
                    int space = rest.IndexOf(' ');
                    if (space > 0) rest = rest.Substring(0, space);
                    if (rest.Length == 0) continue;

                    if (!File.Exists(Repo.Path_(rest.Replace('/', Path.DirectorySeparatorChar))))
                        broken.Add(Path.GetFileName(file) + " runs " + rest);
                }
            }

            Assert.True(broken.Count == 0,
                "A GitHub workflow runs a script that is not in the repository:" + Environment.NewLine +
                Repo.Bullets(broken));
        }
    }
}
