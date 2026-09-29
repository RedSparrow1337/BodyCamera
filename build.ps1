param(
    [string]$SptRoot = "D:\SPT"
)

dotnet build -c Release -p:SptRoot="$SptRoot"
