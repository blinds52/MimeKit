﻿//
// TnefTests.cs
//
// Author: Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2014 Xamarin Inc. (www.xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.IO;
using System.Text;

using MimeKit;
using MimeKit.IO;
using MimeKit.Tnef;
using MimeKit.IO.Filters;

using NUnit.Framework;

namespace UnitTests {
	[TestFixture]
	public class TnefTests
	{
		static void ExtractRecipientTable (TnefReader reader, MimeMessage message)
		{
			var prop = reader.TnefPropertyReader;

			// Note: The RecipientTable uses rows of properties...
			while (prop.ReadNextRow ()) {
				InternetAddressList list = null;
				string name = null, addr = null;

				while (prop.ReadNextProperty ()) {
					switch (prop.PropertyTag.Id) {
					case TnefPropertyId.RecipientType:
						int recipientType = prop.ReadValueAsInt32 ();
						switch (recipientType) {
						case 1: list = message.To; break;
						case 2: list = message.Cc; break;
						case 3: list = message.Bcc; break;
						default:
							Assert.Fail ("Invalid recipient type.");
							break;
						}
						Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, recipientType);
						break;
					case TnefPropertyId.TransmitableDisplayName:
						if (string.IsNullOrEmpty (name)) {
							name = prop.ReadValueAsString ();
							Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, name);
						} else {
							Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, prop.ReadValueAsString ());
						}
						break;
					case TnefPropertyId.DisplayName:
						name = prop.ReadValueAsString ();
						Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, name);
						break;
					case TnefPropertyId.EmailAddress:
						if (string.IsNullOrEmpty (addr)) {
							addr = prop.ReadValueAsString ();
							Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, addr);
						} else {
							Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, prop.ReadValueAsString ());
						}
						break;
					case TnefPropertyId.SmtpAddress:
						// The SmtpAddress, if it exists, should take precedence over the EmailAddress
						// (since the SmtpAddress is meant to be used in the RCPT TO command).
						addr = prop.ReadValueAsString ();
						Console.WriteLine ("RecipientTable Property: {0} = {1}", prop.PropertyTag.Id, addr);
						break;
					default:
						Console.WriteLine ("RecipientTable Property (unhandled): {0} = {1}", prop.PropertyTag.Id, prop.ReadValue ());
						break;
					}
				}

				Assert.NotNull (list, "The recipient type was never specified.");
				Assert.NotNull (addr, "The address was never specified.");

				if (list != null)
					list.Add (new MailboxAddress (name, addr));
			}
		}

		static void ExtractMapiProperties (TnefReader reader, MimeMessage message, BodyBuilder builder)
		{
			var prop = reader.TnefPropertyReader;

			while (prop.ReadNextProperty ()) {
				switch (prop.PropertyTag.Id) {
				case TnefPropertyId.InternetMessageId:
					if (prop.PropertyTag.ValueTnefType == TnefPropertyType.String8 ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Unicode) {
						message.MessageId = prop.ReadValueAsString ();
						Console.WriteLine ("Message Property: {0} = {1}", prop.PropertyTag.Id, message.MessageId);
					} else {
						Assert.Fail ("Unknown property type for Message-Id: {0}", prop.PropertyTag.ValueTnefType);
					}
					break;
				case TnefPropertyId.Subject:
					if (prop.PropertyTag.ValueTnefType == TnefPropertyType.String8 ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Unicode) {
						message.Subject = prop.ReadValueAsString ();
						Console.WriteLine ("Message Property: {0} = {1}", prop.PropertyTag.Id, message.Subject);
					} else {
						Assert.Fail ("Unknown property type for Subject: {0}", prop.PropertyTag.ValueTnefType);
					}
					break;
				case TnefPropertyId.BodyHtml:
					if (prop.PropertyTag.ValueTnefType == TnefPropertyType.String8 ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Unicode ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Binary) {
						builder.HtmlBody = prop.ReadValueAsString ();
						Console.WriteLine ("Message Property: {0} = {1}", prop.PropertyTag.Id, builder.HtmlBody);
					} else {
						Assert.Fail ("Unknown property type for BodyHtml: {0}", prop.PropertyTag.ValueTnefType);
					}
					break;
				case TnefPropertyId.Body:
					if (prop.PropertyTag.ValueTnefType == TnefPropertyType.String8 ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Unicode ||
						prop.PropertyTag.ValueTnefType == TnefPropertyType.Binary) {
						builder.TextBody = prop.ReadValueAsString ();
						Console.WriteLine ("Message Property: {0} = {1}", prop.PropertyTag.Id, builder.TextBody);
					} else {
						Assert.Fail ("Unknown property type for Body: {0}", prop.PropertyTag.ValueTnefType);
					}
					break;
				default:
					Console.WriteLine ("Message Property (unhandled): {0} = {1}", prop.PropertyTag.Id, prop.ReadValue ());
					break;
				}
			}
		}

		static void ExtractAttachments (TnefReader reader, BodyBuilder builder)
		{
			var prop = reader.TnefPropertyReader;
			bool attachRenderData = false;

			do {
				if (reader.AttributeLevel != TnefAttributeLevel.Attachment)
					Assert.Fail ("Expected attachment attribute level: {0}", reader.AttributeLevel);

				if (reader.AttributeTag == TnefAttributeTag.AttachRenderData)
					attachRenderData = true;

				if (attachRenderData) {
					var attachment = new MimePart ();
					MimeMessage embedded = null;
					string text;

					while (prop.ReadNextProperty ()) {
						switch (prop.PropertyTag.Id) {
						case TnefPropertyId.AttachLongFilename:
							attachment.FileName = prop.ReadValueAsString ();
							break;
						case TnefPropertyId.AttachFilename:
							if (attachment.FileName == null)
								attachment.FileName = prop.ReadValueAsString ();
							break;
						case TnefPropertyId.AttachContentLocation:
							text = prop.ReadValueAsString ();
							if (Uri.IsWellFormedUriString (text, UriKind.Absolute))
								attachment.ContentLocation = new Uri (text, UriKind.Absolute);
							else if (Uri.IsWellFormedUriString (text, UriKind.Relative))
								attachment.ContentLocation = new Uri (text, UriKind.Relative);
							break;
						case TnefPropertyId.AttachContentBase:
							text = prop.ReadValueAsString ();
							attachment.ContentBase = new Uri (text, UriKind.Absolute);
							break;
						case TnefPropertyId.AttachContentId:
							attachment.ContentId = prop.ReadValueAsString ();
							break;
						case TnefPropertyId.AttachDisposition:
							text = prop.ReadValueAsString ();
							if (attachment.ContentDisposition == null)
								attachment.ContentDisposition = new ContentDisposition (text);
							else
								attachment.ContentDisposition.Disposition = text;
							break;
						case TnefPropertyId.AttachData:
							if (prop.IsEmbeddedMessage) {
								using (var er = prop.GetEmbeddedMessageReader ()) {
									embedded = ExtractTnefMessage (er);
								}

								break;
							}

							using (var attachData = prop.GetRawValueReadStream ()) {
								var filter = new BestEncodingFilter ();
								var content = new MemoryBlockStream ();

								using (var filtered = new FilteredStream (content)) {
									filtered.Add (filter);

									attachData.CopyTo (filtered, 4096);
									filtered.Flush ();
								}

								content.Position = 0;

								attachment.ContentTransferEncoding = filter.GetBestEncoding (EncodingConstraint.SevenBit);
								attachment.ContentObject = new ContentObject (content, ContentEncoding.Default);
								embedded = null;
							}
							break;
						default:
							Console.WriteLine ("Attachment Property (unhandled): {0} = {1}", prop.PropertyTag.Id, prop.ReadValue ());
							break;
						}
					}

					if (embedded != null) {
						var rfc822 = new MessagePart ();
						rfc822.ContentDisposition = attachment.ContentDisposition;
						rfc822.ContentLocation = attachment.ContentLocation;
						rfc822.ContentBase = attachment.ContentBase;
						rfc822.ContentId = attachment.ContentId;

						rfc822.Message = embedded;

						builder.Attachments.Add (rfc822);
					} else {
						builder.Attachments.Add (attachment);
					}
				} else {
					Console.WriteLine ("Attachment ATtribute (unhandled): {0} = {1}", reader.AttributeTag, prop.ReadValue ());
				}
			} while (reader.ReadNextAttribute ());
		}

		static MimeMessage ExtractTnefMessage (TnefReader reader)
		{
			var builder = new BodyBuilder ();
			var message = new MimeMessage ();

			while (reader.ReadNextAttribute ()) {
				if (reader.AttributeLevel == TnefAttributeLevel.Attachment)
					break;

				if (reader.AttributeLevel != TnefAttributeLevel.Message)
					Assert.Fail ("Unknown attribute level.");

				var prop = reader.TnefPropertyReader;

				switch (reader.AttributeTag) {
				case TnefAttributeTag.RecipientTable:
					ExtractRecipientTable (reader, message);
					break;
				case TnefAttributeTag.MapiProperties:
					ExtractMapiProperties (reader, message, builder);
					break;
				case TnefAttributeTag.DateSent:
					message.Date = prop.ReadValueAsDateTime ();
					Console.WriteLine ("Message Attribute: {0} = {1}", reader.AttributeTag, message.Date);
					break;
				case TnefAttributeTag.Body:
					builder.TextBody = prop.ReadValueAsString ();
					Console.WriteLine ("Message Attribute: {0} = {1}", reader.AttributeTag, builder.TextBody);
					break;
				case TnefAttributeTag.TnefVersion:
					Console.WriteLine ("Message Attribute: {0} = {1}", reader.AttributeTag, prop.ReadValueAsInt32 ());
					break;
				case TnefAttributeTag.OemCodepage:
					int codepage = prop.ReadValueAsInt32 ();
					try {
						var encoding = Encoding.GetEncoding (codepage);
						Console.WriteLine ("Message Attribute: OemCodepage = {0}", encoding.HeaderName);
					}
					catch {
						Console.WriteLine ("Message Attribute: OemCodepage = {0}", codepage);
					}
					break;
				default:
					Console.WriteLine ("Message Attribute (unhandled): {0} = {1}", reader.AttributeTag, prop.ReadValue ());
					break;
				}
			}

			if (reader.AttributeLevel == TnefAttributeLevel.Attachment) {
				ExtractAttachments (reader, builder);
			} else {
				Console.WriteLine ("no attachments");
			}

			message.Body = builder.ToMessageBody ();

			return message;
		}

		static MimeMessage ParseTnefMessage (string path)
		{
			using (var reader = new TnefReader (File.OpenRead (path))) {
				return ExtractTnefMessage (reader);
			}
		}

		static void TestTnefParser (string path)
		{
			var message = ParseTnefMessage (path + ".tnef");
			var names = File.ReadAllLines (path + ".list");

			foreach (var name in names) {
				bool found = false;

				foreach (var part in message.BodyParts) {
					if (part.FileName == name) {
						found = true;
						break;
					}
				}

				if (!found)
					Assert.Fail ("Failed to locate attachment: {0}", name);
			}
		}

		[Test]
		public void TestBody ()
		{
			TestTnefParser ("../../TestData/tnef/body");
		}

		[Test]
		public void TestDataBeforeName ()
		{
			TestTnefParser ("../../TestData/tnef/data-before-name");
		}

		[Test]
		public void TestGarbageAtEnd ()
		{
			TestTnefParser ("../../TestData/tnef/garbage-at-end");
		}

		[Test]
		public void TestLongFileName ()
		{
			TestTnefParser ("../../TestData/tnef/long-filename");
		}

		[Test]
		public void TestMapiAttachDataObj ()
		{
			TestTnefParser ("../../TestData/tnef/MAPI_ATTACH_DATA_OBJ");
		}

		[Test]
		public void TestMapiObject ()
		{
			TestTnefParser ("../../TestData/tnef/MAPI_OBJECT");
		}

		[Test]
		public void TestMissingFileNames ()
		{
			TestTnefParser ("../../TestData/tnef/missing-filenames");
		}

		[Test]
		public void TestMultiNameProperty ()
		{
			TestTnefParser ("../../TestData/tnef/multi-name-property");
		}

		[Test]
		public void TestOneFile ()
		{
			TestTnefParser ("../../TestData/tnef/one-file");
		}

		[Test]
		public void TestRtf ()
		{
			TestTnefParser ("../../TestData/tnef/rtf");
		}

		[Test]
		public void TestTriples ()
		{
			TestTnefParser ("../../TestData/tnef/triples");
		}

		[Test]
		public void TestTwoFiles ()
		{
			TestTnefParser ("../../TestData/tnef/two-files");
		}
	}
}