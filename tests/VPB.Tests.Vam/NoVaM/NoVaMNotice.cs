using Xunit;

namespace VPB.Tests
{
    public class NoVaMNotice
    {
        [Fact(Skip = "No VaM install found. Set <VaMPath> in VPB.local.props (or install VaM at C:\\vam) and rebuild.")]
        public void VamSuiteRequiresAVamInstall()
        {
        }
    }
}
