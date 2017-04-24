using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Net;
using System.Net.Mail;
using System.IO;
using Newtonsoft.Json.Linq;
using CodeFirstWebFramework;

namespace AccountServer {
	/// <summary>
	/// Customers and suppliers have some code in common
	/// </summary>
	public class Customer : CustomerSupplier {

		public Customer()
			: base("C", Acct.SalesLedger, DocType.Invoice, DocType.CreditMemo, DocType.Payment) {
			insertMenuOption(new MenuOption("Products", "/customer/products.html"));
		}

		/// <summary>
		/// All documents for this customer
		/// </summary>
		public object DetailListing(int id) {
			return Database.Query("Document.*, DocType, Amount, Outstanding",
				"WHERE AccountId = " + (int)LedgerAccount + " AND NameAddressId = " + id + " ORDER BY DocumentDate, idDocument",
				"Document", "Journal");
		}

		/// <summary>
		/// Get an individual document for editing
		/// </summary>
		public void Document(int id, DocType type) {
			JObject record = document(id, type);	// implemented in base class
			record["detail"] = Database.Query("idJournal, DocumentId, ProductId, Line.VatCodeId, VatRate, JournalNum, Journal.AccountId, Memo, UnitPrice, Qty, LineAmount, VatAmount, Unit",
					"WHERE Journal.DocumentId = " + id + " AND idLine IS NOT NULL ORDER BY JournalNum",
					"Document", "Journal", "Line");
			record.Add("Products", SelectProducts());
			Record = record;
		}

		/// <summary>
		/// Work out the changes when a Payment is edited
		/// </summary>
		protected override void calculatePaymentChanges(PaymentDocument json, decimal amount, out decimal changeInDocumentAmount, out decimal changeInOutstanding) {
			PaymentHeader document = json.header;
			PaymentHeader original = getDocument(document);
			changeInDocumentAmount = document.DocumentAmount - original.DocumentAmount;
			changeInOutstanding = original.DocumentOutstanding + changeInDocumentAmount - amount;
			Utils.Check(document.DocumentOutstanding == document.Remaining, "Remaining {0:0.00} does not agree with outstanding {1:0.00}",
				document.Remaining, document.DocumentOutstanding);
		}

		public void Print(int id) {
			prepareInvoice(id);
			WriteResponse(LoadTemplate("customer/print", this), "text/html", System.Net.HttpStatusCode.OK);
		}

		public void Download(int id) {
			Response.Headers["Content-disposition"] = "attachment; filename=S" + id + ".html";
			Print(id);
		}

		public AjaxReturn Email(int id) {
			Extended_Document header = prepareInvoice(id);
			NameAddress customer = Database.Get<NameAddress>((int)header.DocumentNameAddressId);
			Utils.Check(!string.IsNullOrEmpty(Settings.CompanyEmail), "Company has no email address");
			Utils.Check(!string.IsNullOrEmpty(customer.Email), "Customer has no email address");
			Utils.Check(customer.Email.Contains('@'), "Customer has an invalid email address");
			((JObject)((JObject)Record)["header"])["doctype"] = header.DocType.ToLower();
			((JObject)Record)["customer"] = customer.ToJToken();
			string text = LoadTemplate("customer/email.txt", this);
			string subject = Utils.NextToken(ref text, "\n").Trim();
			using (MemoryStream stream = new MemoryStream(Encoding.GetBytes(LoadTemplate("customer/print", this)))) {
				// Create a message and set up the recipients.
				MailMessage message = new MailMessage();
				message.From = new MailAddress(Settings.CompanyEmail);
				foreach(string e in customer.Email.Split(','))
					message.To.Add(e);
				message.Bcc.Add(Settings.CompanyEmail);
				message.Subject = subject;
				message.Body = text;
				// Create  the file attachment for this e-mail message.
				Attachment data = new Attachment(stream, Settings.CompanyName + "Invoice" + header.DocumentIdentifier + ".html", "text/html");
				// Add the file attachment to this e-mail message.
				message.Attachments.Add(data);

				//Send the message.
				SmtpClient client = new SmtpClient(Settings.MailServer);
				// Add credentials if the SMTP server requires them.
				client.Credentials = new NetworkCredential(Settings.MailUserName, Settings.MailPassword);
				client.Port = Settings.MailPort;
				client.EnableSsl = Settings.MailSSL;
				client.Send(message);
			}
			return new AjaxReturn() { message = "Email sent to " + customer.Email };
		}

		/// <summary>
		/// Prepare an invoice for printing/saving/emailing
		/// </summary>
		Extended_Document prepareInvoice(int id) {
			Extended_Document header = getDocument<Extended_Document>(id);
			Utils.Check(header.idDocument != null, "Document not found");
			DocType type = (DocType)header.DocumentTypeId;
			checkNameType(header.DocumentNameAddressId, NameType);
			Title = Title.Replace("Document", type.UnCamel());
			if (SignFor(type) > 0) {
				header.DocumentAmount = -header.DocumentAmount;
				header.DocumentOutstanding = -header.DocumentOutstanding;
			}
			List<Extended_Line> detail = Database.Query<Extended_Line>("SELECT * FROM Extended_Line WHERE DocumentId = " + id + " ORDER BY JournalNum").ToList();
			JObject record = new JObject().AddRange("header", header, "detail", detail);
			decimal net = 0, vat = 0;
			foreach (Extended_Line d in detail) {
				net += d.LineAmount;
				vat += d.VatAmount;
			}
			record["TotalVat"] = vat;
			record["TotalNet"] = net;
			record["Total"] = net + vat;
			Record = record;
			return header;
		}

		public void Products() {
		}

		public object ProductsListing() {
			return Database.Query("*", "ORDER BY ProductName", "Product");
		}

		public void Product(int id) {
			JObject inUse = Database.QueryOne("SELECT idLine FROM Line WHERE ProductId = " + id);
			Record = new JObject().AddRange("header", Database.Get<FullProduct>(id),
				"canDelete", inUse == null || inUse.IsAllNull(),
				"VatCodes", SelectVatCodes(),
				"Accounts", SelectIncomeAccounts());
		}

		public AjaxReturn ProductDelete(int id) {
			AjaxReturn result = new AjaxReturn();
			try {
				Database.Delete("Product", id, true);
				result.message = "Product deleted";
			} catch {
				result.error = "Cannot delete - Product in use";
			}
			return result;
		}

		public AjaxReturn ProductPost(Product json) {
			return PostRecord(json, true);
		}

	}

#pragma warning disable 0649
	class FullProduct : Product {
		public string Code;
		public string AccountName;
	}
#pragma warning restore 0649

}

