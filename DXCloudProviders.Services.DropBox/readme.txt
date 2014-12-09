Usage in you existing application:

Check the web.config for required settings and register an app with dropbox 

Create an empty ASPX Page e.g. Signing.aspx which will handle the OAuth authentication and place in it's Page_Load:

    public partial class SignIn : System.Web.UI.Page
    {
		  protected void Page_Load(object sender, EventArgs e)
		  {
				DropBoxAuthentication.ProcessAuthorized(Context);				
		  }
    }

Make sure that in the web.config the setting is correct.
If you have a different pagename, do add the ?oauth_callback=Dropbox in the web.config like below:

	 <add key="Dropbox_RedirectUrl " value="~/Signin.aspx?oauth_callback=Dropbox" />

In the OnInit of the Page or UserControl containing a filemanager, place the following code:

		  protected override void OnInit(EventArgs e)
		  {
				base.OnInit(e);
				
				DropBoxFileSystemProvider.InitializeFileManager(ASPxFileManager1);
				ASPxFileManager1.CustomFileSystemProvider = new DropBoxFileSystemProvider("");

				// .... other code ....

		  }
