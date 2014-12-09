using DevExpress.Utils.OAuth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DXCloudProviders.Services.DropBox
{
	 static class OAuthExtensions
	 {
		  public static IToken GetDropBoxAccessToken(this Consumer oauth, string code)
		  {
				if (oauth.AccessToken != null && !oauth.AccessToken.IsEmpty)
				{
					 return oauth.AccessToken;
				}
				using (WebResponse httpResponse = Consumer.CreateHttpRequest(oauth.GetAccessTokenUrl(code), oauth.HttpMethod, oauth.Cookies).GetResponse())
				{
					 Dropbox_Token tokenRespone = (Dropbox_Token)new DataContractJsonSerializer(typeof(Dropbox_Token)).ReadObject(httpResponse.GetResponseStream());
					 Parameters parameters = new Parameters();
					 parameters.Add(new Parameter("access_token", tokenRespone.access_token));
					 parameters.Add(new Parameter("token_type", tokenRespone.token_type));
					 parameters.Add(new Parameter("uid", tokenRespone.uid));
					 oauth.AccessToken = new Token(parameters, oauth.ConsumerKey, oauth.ConsumerSecret, oauth.CallbackUri.ToString(), "2.0");
					 return oauth.AccessToken;
				}
		  }

		  public static byte[] GetResourceBytes(this Consumer oauth, Uri requestUri, IToken token, string version)
		  {
				if (version == null || version.Length == 0)
				{
					 version = "1.0";
				}
				HttpWebRequest request = Consumer.CreateHttpRequest(requestUri, oauth.HttpMethod, oauth.Cookies, token, oauth.Signature, version);
				try
				{
					 HttpStatusCode statusCode = HttpStatusCode.OK;
					 string statusDescription = String.Empty;
					 string redirectUri = String.Empty;

					 using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
					 {
						  statusCode = response.StatusCode;
						  statusDescription = response.StatusDescription;
						  redirectUri = response.Headers["Location"];
						  if (statusCode == HttpStatusCode.OK)
						  {
								return response.GetResponseStream().ReadAllBytes();
						  }
					 }
					 System.Diagnostics.Debug.Assert(statusCode != HttpStatusCode.OK, "statusCode != HttpStatusCode.OK");
					 if (statusCode == HttpStatusCode.Redirect && !String.IsNullOrEmpty(redirectUri))
					 {
						  request = Consumer.CreateHttpRequest(new Uri(redirectUri, UriKind.Absolute), oauth.HttpMethod, oauth.Cookies, token, oauth.Signature, version);
						  using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
						  {
								statusCode = response.StatusCode;
								statusDescription = response.StatusDescription;
								if (statusCode == HttpStatusCode.OK)
								{
									 return response.GetResponseStream().ReadAllBytes();
								}
						  }
					 }
					 throw new WebException(statusDescription);
				}
				catch
				{
					 throw;
				}
		  }

		  public static string SubmitResource(this Consumer oauth, Uri requestUri, IToken token, string version, byte[] content, int contentLength)
		  {
				if (version == null || version.Length == 0)
				{
					 version = "1.0";
				}
				HttpWebRequest request = Consumer.CreateHttpRequest(requestUri, oauth.HttpMethod, oauth.Cookies, token, oauth.Signature, version);
				try
				{
					 request.KeepAlive = true;
					 request.Timeout = 1000 * 60 * 60 * 24;
					 using (Stream requestStream = request.GetRequestStream())
					 {
						  requestStream.Write(content, 0, contentLength);
					 }

					 HttpStatusCode statusCode = HttpStatusCode.OK;
					 string statusDescription = String.Empty;
					 string redirectUri = String.Empty;

					 using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
					 {
						  statusCode = response.StatusCode;
						  statusDescription = response.StatusDescription;
						  redirectUri = response.Headers["Location"];
						  if (statusCode == HttpStatusCode.OK)
						  {
								return Consumer.ParseResource(response);
						  }
					 }
					 System.Diagnostics.Debug.Assert(statusCode != HttpStatusCode.OK, "statusCode != HttpStatusCode.OK");
					 if (statusCode == HttpStatusCode.Redirect && !String.IsNullOrEmpty(redirectUri))
					 {
						  request = Consumer.CreateHttpRequest(new Uri(redirectUri, UriKind.Absolute), oauth.HttpMethod, oauth.Cookies, token, oauth.Signature, version);
						  request.KeepAlive = true;
						  request.Timeout = 1000 * 60 * 60 * 24;
						  using (Stream requestStream = request.GetRequestStream())
						  {
								requestStream.Write(content, 0, contentLength);
						  }

						  using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
						  {
								statusCode = response.StatusCode;
								statusDescription = response.StatusDescription;
								if (statusCode == HttpStatusCode.OK)
								{
									 return Consumer.ParseResource(response); ;
								}
						  }
					 }
					 throw new WebException(statusDescription);
				}
				catch
				{
					 throw;
				}
		  }

		  public static byte[] ReadAllBytes(this Stream input)
		  {
				byte[] buffer = new byte[16 * 1024];
				using (MemoryStream ms = new MemoryStream())
				{
					 int read;
					 while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
					 {
						  ms.Write(buffer, 0, read);
					 }
					 return ms.ToArray();
				}
		  }

		  #region Helper classes for JSON deserialisation

		  [DataContract]
		  class Dropbox_Token
		  {
				[DataMember]
				public string access_token { get; internal set; }
				[DataMember]
				public string token_type { get; internal set; }
				[DataMember]
				public string uid { get; internal set; }
		  }

		  #endregion


	 }
}
