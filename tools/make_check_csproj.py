"""Regenerate VPBCheck.csproj from VPB.csproj.

VPB.csproj targets .NET Framework 3.5, which no targeting pack on a modern machine
provides, so `dotnet build VPB.csproj` dies at MSB3644 before the compiler ever runs.
This produces a sibling project that compiles the exact same sources against VaM's own
Managed folder -- the real Mono profile the plugin loads into -- so plugin code can be
compile-verified without a 3.5 developer pack.

It builds only; the output is never shipped. Run from the repo root:

    python tools/make_check_csproj.py && dotnet build VPBCheck.csproj
"""

import re

VAM = r"$(VaMPath)\VaM_Data\Managed"

FROM_VAM = ["mscorlib", "System", "System.Core", "System.Xml", "System.Data", "System.Drawing"]
DROP = ["System.Xml.Linq", "System.Data.DataSetExtensions"]


def reference(name):
    return (
        '<Reference Include="%s">\n'
        "      <HintPath>%s\\%s.dll</HintPath>\n"
        "      <Private>false</Private>\n"
        "    </Reference>" % (name, VAM, name)
    )


def main():
    src = open("VPB.csproj", encoding="utf-8-sig").read()

    src = src.replace(
        "<TargetFrameworkVersion>v3.5</TargetFrameworkVersion>",
        "<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>\n"
        "    <NoStdLib>true</NoStdLib>\n"
        "    <NoConfig>true</NoConfig>\n"
        "    <ImplicitlyExpandDesignTimeFacades>false</ImplicitlyExpandDesignTimeFacades>",
    )
    src = src.replace("<AssemblyName>VPB</AssemblyName>", "<AssemblyName>VPBCheck</AssemblyName>")
    src = src.replace("<OutputPath>bin\\Debug\\</OutputPath>", "<OutputPath>bin\\Check\\</OutputPath>")
    src = src.replace("<OutputPath>bin\\Release\\</OutputPath>", "<OutputPath>bin\\Check\\</OutputPath>")

    for name in DROP:
        src = src.replace('<Reference Include="%s" />' % name, "")

    # Accessibility is a desktop-framework assembly VaM has no counterpart for; the slot
    # is reused for mscorlib, which NoStdLib means we now have to name ourselves.
    src = src.replace('<Reference Include="Accessibility" />', reference("mscorlib"))

    for name in FROM_VAM:
        if name == "mscorlib":
            continue
        bare = '<Reference Include="%s" />' % name
        if bare in src:
            src = src.replace(bare, reference(name))
        else:
            src = src.replace('<Reference Include="System.Core">', reference(name) + '\n    <Reference Include="System.Core">', 1)

    src = re.sub(r'<Target Name="(PostBuild|AfterBuild)".*?</Target>', "", src, flags=re.S)
    src = re.sub(r"<PostBuildEvent>.*?</PostBuildEvent>", "", src, flags=re.S)

    open("VPBCheck.csproj", "w", encoding="utf-8").write(src)
    print("VPBCheck.csproj regenerated from VPB.csproj")


if __name__ == "__main__":
    main()
