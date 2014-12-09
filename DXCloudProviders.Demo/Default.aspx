<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="DXCloudProviders.Demo.Default" %>

<%@ Register Assembly="DevExpress.Web.v14.2, Version=14.2.3.0, Culture=neutral, PublicKeyToken=b88d1754d700e49a" Namespace="DevExpress.Web" TagPrefix="dx" %>

<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title></title>
</head>
<body>
    <form id="form1" runat="server">
    <div>
		  <h1>Using the ASPxFileManager with Dropbox</h1>		  
		  <asp:LinkButton runat="server" ID="btLogout" Text="Logout (and loose Dropbox account)" Visible="false" OnClick="btLogout_Click">

		  </asp:LinkButton>		  		  
		  <br />
		  <dx:ASPxFileManager ID="ASPxFileManager1" runat="server" ClientInstanceName="dxFm">
            <ClientSideEvents
					 CustomCommand="function(s, e) {
										  var service = '';
						              switch(e.commandName) {
												case 'Dropbox':
												case 'Blob':
													 service = '?Service=' + e.commandName;
													 break;
										  }
										  window.location.replace('/Default.aspx' + service);
									 }" />
            <Settings RootFolder="~\" ThumbnailFolder="~\Thumb\" />
            <SettingsFileList>
					 <ThumbnailsViewSettings ThumbnailSize="64px" />
				</SettingsFileList>
            <SettingsEditing AllowCopy="True" AllowCreate="True" AllowDelete="True" AllowMove="True" AllowRename="True" />
            <SettingsFolders EnableCallBacks="True" />
            <SettingsToolbar ShowDownloadButton="True" >
					 <Items>
						  <dx:FileManagerToolbarCreateButton>
						  </dx:FileManagerToolbarCreateButton>
						  <dx:FileManagerToolbarRenameButton>
						  </dx:FileManagerToolbarRenameButton>
						  <dx:FileManagerToolbarMoveButton>
						  </dx:FileManagerToolbarMoveButton>
						  <dx:FileManagerToolbarCopyButton>
						  </dx:FileManagerToolbarCopyButton>
						  <dx:FileManagerToolbarDeleteButton>
						  </dx:FileManagerToolbarDeleteButton>
						  <dx:FileManagerToolbarRefreshButton>
						  </dx:FileManagerToolbarRefreshButton>
						  <dx:FileManagerToolbarDownloadButton BeginGroup="True">
						  </dx:FileManagerToolbarDownloadButton>
						  <dx:FileManagerToolbarCustomButton CommandName="Local" GroupName="FS" Text="Local" BeginGroup="True">
						  </dx:FileManagerToolbarCustomButton>
						  <dx:FileManagerToolbarCustomButton CommandName="Dropbox" GroupName="FS" Text="Dropbox">
						  </dx:FileManagerToolbarCustomButton>
						  <dx:FileManagerToolbarCustomButton CommandName="Blob" GroupName="FS" Text="Azure">
						  </dx:FileManagerToolbarCustomButton>
					 </Items>
				</SettingsToolbar>
				<SettingsUpload UseAdvancedUploadMode="True">
					 <AdvancedModeSettings EnableMultiSelect="true" />
				</SettingsUpload>
        </dx:ASPxFileManager>
    </div>
    </form>
</body>
</html>
