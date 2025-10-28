Place the following SAP Crystal Reports assemblies in this folder (licensed binaries are not included in the repo):
- CrystalDecisions.CrystalReports.Engine.dll
- CrystalDecisions.ReportSource.dll
- CrystalDecisions.Shared.dll
- SAPBusinessObjects.WPF.Viewer.dll
- SAPBusinessObjects.WPF.ViewerShared.dll

These will be referenced by the project via HintPath and copied locally at build (Private=true).