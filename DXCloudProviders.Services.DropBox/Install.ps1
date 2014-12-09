param($installPath, $toolsPath, $package, $project)


$exists = Get-ItemProperty "HKLM:\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*" | where {$_.DisplayVersion -match "14.2.3"} | select DisplayName
if (-Not $exists) {
	 throw "We have determined that you haven't installed the proper version of DevExpress. Installation will be rolled back.";
}