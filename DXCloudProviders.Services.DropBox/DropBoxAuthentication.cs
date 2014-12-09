using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Configuration;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using DevExpress.Utils.OAuth;    

namespace DXCloudProviders.Services.DropBox
{
	 public static class DropBoxAuthentication
	 {
		  public const string QueryNameOAuthCallback = "oauth_callback";
		  public const string QueryValOAuthCallback = "Dropbox";
		  public const string QueryNameAccessToken = "code";

		  public const string SessionNameTicket = "DXFileManDropBox";
		  public const string SessionNameReturnUrl = "DXFileManDropBoxReturnUrl";

		  public static string Key { get { return ConfigurationManager.AppSettings["Dropbox_Consumer_Key"]; } }
		  public static string Secret { get { return ConfigurationManager.AppSettings["Dropbox_Consumer_Secret"]; } }
		  public static string RedirectUrl { get { return ConfigurationManager.AppSettings["Dropbox_RedirectUrl"]; } }

		  public static Consumer CreateConsumerForDropbox(Uri callback, string httpMethod = "GET")
		  {
				Consumer oauth = new Consumer()
				{
					 AuthorizeUri = new Uri("https://www.dropbox.com/1/oauth2/authorize?response_type=code", UriKind.Absolute),
					 AccessUri = new Uri("https://api.dropbox.com/1/oauth2/token?grant_type=authorization_code", UriKind.Absolute),
					 ConsumerKey = Key,
					 ConsumerSecret = Secret,
					 HttpMethod = httpMethod,
					 Signature = Signature.HMACSHA1
				};

				if (callback != null)
				{
					 oauth.CallbackUri = new Url(callback, String.Empty).ToUrl(null).ToUri(new Parameter(QueryNameOAuthCallback, QueryValOAuthCallback));
				}

				return oauth;
		  }

		  public static string GetAuthorizeUriForDropbox(Uri callback)
		  {
				return CreateConsumerForDropbox(callback).GetAuthorizeTokenUrl("2.0").ToString();
		  }

		  public static void Signout(HttpContext ctx, string redirectUrl)
		  {
				if (ctx == null)
					 throw new ArgumentNullException("ctx");

				Signout(new HttpContextWrapper(ctx), redirectUrl);
		  }

		  public static void Signout(HttpContextBase ctx, string redirectUrl)
		  {
				if (ctx == null)
					 throw new ArgumentNullException("ctx");

				ctx.Session.Remove(SessionNameTicket);
				ctx.Session.Remove(SessionNameReturnUrl);


				System.Web.UI.Page pg = ctx.Handler as System.Web.UI.Page;
				if (pg != null && pg.IsCallback)
					 DevExpress.Web.ASPxEdit.RedirectOnCallback(redirectUrl ?? ctx.Request.Url.ToString());
				else
				{
					 ctx.Response.Redirect(redirectUrl ?? ctx.Request.Url.ToString(), false);
					 ctx.ApplicationInstance.CompleteRequest();
				}
		  }

		  public static void ProcessAuthorized(HttpContext ctx)
		  {
				if (ctx == null)
					 return;
				ProcessAuthorized(new HttpContextWrapper(ctx));
		  }

		  
		  public static void ProcessAuthorized(HttpContextBase ctx)
		  {
				if (ctx == null)
					 return;

				string callback = ctx.Request.QueryString[QueryNameOAuthCallback];
				if (String.Compare(callback, QueryValOAuthCallback, true) == 0)
				{
					 if (!String.IsNullOrEmpty(ctx.Request.QueryString["error"]))
					 {
					 ctx.Response.Write(String.Format("<html><head><title>Error</title></head><body><p>{0}</p><a href=\"{1}\">Home</a></body></html>", 
						  HttpUtility.UrlDecode(ctx.Request.QueryString["error_description"] ?? "The application was not authenticated"),
						  VirtualPathUtility.ToAbsolute("~/")));
					 return;
					 }
					 IToken accessToken = null;
					 /* OAuth 2.0 code that passed back from provider */
					 string code = ctx.Request.QueryString[QueryNameAccessToken];
					 /* Was access was not granted ? */
					 if (String.IsNullOrEmpty(code))
						  return;

					 Consumer oauth = DropBoxAuthentication.CreateConsumerForDropbox(ctx.Request.Url);
					 oauth.HttpMethod = "POST";
					 accessToken = oauth.GetDropBoxAccessToken(code);
					 Dropbox_Me ticket = FromDropbox(oauth);
					 if (ticket.IsAuthenticated)
					 {
						  ticket.SetAccessToken(accessToken.Value);
						  ctx.Session[SessionNameTicket] = ticket;
						  string returnUrl = "~/";
						  if (!String.IsNullOrEmpty((string)ctx.Session[SessionNameReturnUrl]))
						  {
								returnUrl = (string)ctx.Session[SessionNameReturnUrl];
								ctx.Session.Remove(SessionNameReturnUrl);
								ctx.Response.Redirect(returnUrl);
								return;
						  }
					 }
				}
					
				ctx.Response.Redirect("~/", false);
				ctx.ApplicationInstance.CompleteRequest();
		  }

		  static Dropbox_Me FromDropbox(Consumer auth)
		  {
				if (auth == null) throw new ArgumentNullException("auth");
				if (auth.AccessToken == null) throw new InvalidOperationException();

				Dropbox_Me me = new Dropbox_Me();
				try
				{
					 string data = auth.GetResource(new Uri("https://api.dropbox.com/1/account/info", UriKind.Absolute), auth.AccessToken, "2.0");
					 using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
					 {
						  me = (Dropbox_Me)new DataContractJsonSerializer(typeof(Dropbox_Me)).ReadObject(stream);
					 }
				}
				catch
				{
				}

				return me;
		  }

		  [DataContract]
		  public class Dropbox_Token
		  {
				[DataMember]
				public string access_token { get; set; }
				[DataMember]
				public string token_type { get; set; }
				[DataMember]
				public string uid { get; set; }
		  }
		  [DataContract]
		  public class Dropbox_Me
		  {
				[DataMember]
				public string uid { get; set; }
				[DataMember]
				public string display_name { get; set; }
				[DataMember]
				public string country { get; set; }

				public bool IsAuthenticated { get { return !String.IsNullOrEmpty(uid); } }

				public string AccessToken { get; private set; }
				public void SetAccessToken(string value)
				{
					 AccessToken = value;
				}
		  }


	 }
}
