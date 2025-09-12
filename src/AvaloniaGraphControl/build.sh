dotnet pack src/AvaloniaGraphControl.csproj -c ${CONFIGURATION:-Release}
mkdir -p local_package
dotnet nuget push $(find src -name '*.nupkg') -s DefaultPushSource
