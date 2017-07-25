using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Web;
using System.IO;
using System.Reflection;
using Mustache;
using CodeFirstWebFramework;

#pragma warning disable 0649

namespace AccountServer {

	[Table]
	public class Account : JsonObject {
		/// <summary>
		/// For system accounts <see cref="Acct" />
		/// </summary>
		[Primary]
		public int? idAccount;
		/// <summary>
		/// Account name. Subaccounts are stored as shown - e.g. "Payroll:Taxes".
		/// </summary>
		[Length(75)]
		[Unique("AccountName_UNIQUE")]
		public string AccountName;
		[Length(75)]
		public string AccountDescription;
		/// <summary>
		/// <see cref="AcctType" />
		/// </summary>
		[ForeignKey("AccountType")]
		public int AccountTypeId;
		/// <summary>
		/// Code to allow sorting in non-alphabetical order in reports - e.g. to sort in same order as your accountant uses
		/// </summary>
		public string AccountCode;
		/// <summary>
		/// System account, user cannot edit
		/// </summary>
		public bool Protected;
		/// <summary>
		/// Used only during reconciliation
		/// </summary>
		public decimal? EndingBalance;
		public int NextChequeNumber;
		public int NextDepositNumber;
		/// <summary>
		/// User does not want to see it
		/// </summary>
		public bool HideAccount;
		/// <summary>
		/// For matching statement data pasted from web
		/// </summary>
		[Length(0)]
		public string StatementFormat;
		public override int? Id {
			get { return idAccount; }
			set { idAccount = value; }
		}
	}

	[Table]
	public class AccountType : JsonObject {
		/// <summary>
		/// <see cref="AcctType" />
		/// </summary>
		[Primary]
		public int? idAccountType;
		/// <summary>
		/// P&L or Balance sheet report heading
		/// </summary>
		[DefaultValue("Other")]
		public string Heading;
		[Unique("Name_UNIQUE")]
		public string AcctType;
		/// <summary>
		/// Reverse the sign in P &amp; L and Balance sheet
		/// </summary>
		public bool Negate;
		/// <summary>
		/// True if appears in Balance sheet report
		/// </summary>
		public bool BalanceSheet;
		public override int? Id {
			get { return idAccountType; }
			set { idAccountType = value; }
		}
	}

	[Table]
	public class AuditTrail : JsonObject {
		[Primary]
		public int? idAuditTrail;
		public DateTime DateChanged;
		public string TableName;
		public int? UserId;
		/// <summary>
		/// <see cref="AuditType" />
		/// </summary>
		public int ChangeType;
		/// <summary>
		/// Of the record being audited
		/// </summary>
		public int RecordId;
		/// <summary>
		/// Json data for the record
		/// </summary>
		[Length(0)]
		public string Record;
		public override int? Id {
			get { return idAuditTrail; }
			set { idAuditTrail = value; }
		}
	}

	[Table]
	public class Document : JsonObject {
		[Primary]
		public int? idDocument;
		[Length(0)]
		public string DocumentMemo;
		/// <summary>
		/// <see cref="DocType" />
		/// </summary>
		[ForeignKey("DocumentType")]
		public int DocumentTypeId;
		[Length(0)]
		public string DocumentAddress;
		public DateTime DocumentDate;
		public string DocumentIdentifier;
		/// <summary>
		/// Record of payment to HM which paid the vat in this document
		/// </summary>
		public int? VatPaid;
		public override int? Id {
			get { return idDocument; }
			set { idDocument = value; }
		}
	}

	[Table]
	public class DocumentType : JsonObject {
		/// <summary>
		/// <see cref="DocType" />
		/// </summary>
		[Primary]
		public int? idDocumentType;
		[Unique("DocumentName_UNIQUE")]
		public string DocType;
		/// <summary>
		/// Id of Sales Ledger or Purchase Ledger account for invoices, payments, etc. Otherwise null
		/// </summary>
		[ForeignKey("Account")]
		public int? PrimaryAccountId;
		/// <summary>
		/// "C" for Customer, "S" for Supplier, "O" for Other
		/// </summary>
		[Length(1)]
		[DefaultValue("O")]
		public string NameType;
		/// <summary>
		/// Natural document sign - e.g. -1 for deposits, 1 for cheques.
		/// <see cref="AppModule.SignFor(DocType)"/> 
		/// </summary>
		[DefaultValue("1")]
		public int Sign;
		public override int? Id {
			get { return idDocumentType; }
			set { idDocumentType = value; }
		}
	}

	[Table]
	public class Journal : JsonObject {
		[Primary]
		public int? idJournal;
		[ForeignKey("Document")]
		[Unique("Document_Num")]
		public int DocumentId;
		[ForeignKey("Account")]
		public int AccountId;
		[Length(75)]
		public string Memo;
		[Unique("Document_Num", 1)]
		public int JournalNum;
		public decimal Amount;
		/// <summary>
		/// Unpaid amount for invoices, etc.
		/// </summary>
		public decimal Outstanding;
		/// <summary>
		/// "X" = cleared, "*" = marked for clearing in an incomplete reconciliation
		/// </summary>
		[Length(1)]
		public string Cleared;
		[ForeignKey("NameAddress")]
		public int? NameAddressId;
		public override int? Id {
			get { return idJournal; }
			set { idJournal = value; }
		}
	}

	[Table]
	public class Line : JsonObject {
		[ForeignKey("Journal")]
		[Primary(AutoIncrement = false)]
		public int? idLine;
		public double Qty;
		[ForeignKey("Product")]
		public int? ProductId;
		public decimal LineAmount;
		[ForeignKey("VatCode")]
		public int? VatCodeId;
		public decimal VatRate;
		public decimal VatAmount;
		public override int? Id {
			get { return idLine; }
			set { idLine = value; }
		}
	}

	[Table]
	public class Member : JsonObject {
		[Primary]
		public int? idMember;
		[Unique("MemberNo")]
		public int MemberNo;
		[ForeignKey("MemberType")]
		public int MemberTypeId;
		[ForeignKey("NameAddress")]
		public int NameAddressId;
		public string Title;
		public string FirstName;
		public string LastName;
		public string Name {
			get {
				return FirstName + " " + LastName;
			}
		}
		public decimal PaymentAmount;
		public decimal AmountDue;
		public override int? Id {
			get { return idMember; }
			set { idMember = value; }
		}
	}

	[Table]
	public class MemberType : JsonObject {
		[Primary]
		public int? idMemberType;
		[Unique("MemberTypeName")]
		public string MemberTypeName;
		public decimal AnnualSubscription;
		public int NumberOfPayments = 1;
		public override int? Id {
			get { return idMemberType; }
			set { idMemberType = value; }
		}
	}

	[View(@"SELECT * FROM Member
JOIN MemberType ON idMemberType = MemberTypeId
JOIN NameAddress ON idNameAddress = NameAddressId")]
	public class Full_Member : Member {
		[Field(Type = "string", Heading = "Member Type")]
		public string MemberTypeName;
		[Field(Type = "decimal")]
		public decimal AnnualSubscription;
		[Field(Type = "int", Visible = false)]
		public int NumberOfPayments = 1;
		[Length(0)]
		[Writeable]
		public string Address;
		[Length(15)]
		[Writeable]
		public string PostCode;
		[Writeable]
		public string Telephone;
		[Length(50)]
		[Writeable]
		public string Email;
		[Writeable]
		public string Contact;
		[Field(Heading = "Left")]
		[Writeable]
		public bool Hidden;
	}

	[Table]
	public class NameAddress : JsonObject {
		[Primary]
		public int? idNameAddress;
		/// <summary>
		/// "C" for Customer, "S" for Supplier, "O" for Other
		/// </summary>
		[Length(1)]
		[Unique("Type_Name")]
		public string Type;
		[Length(75)]
		[Nullable]
		[Unique("Type_Name", 1)]
		public string Name;
		[Length(0)]
		public string Address;
		[Length(15)]
		public string PostCode;
		public string Telephone;
		[Length(50)]
		public string Email;
		public string Contact;
		public bool Hidden;
		public override int? Id {
			get { return idNameAddress; }
			set { idNameAddress = value; }
		}
	}

	/// <summary>
	/// Cross-references Invoices to Payments at document level.
	/// Implements a many-to-many relationship.
	/// </summary>
	[Table]
	public class Payments : JsonObject {
		[ForeignKey("Document")]
		[Primary(AutoIncrement = false)]
		public int? idPayment;
		[ForeignKey("Document")]
		[Primary(1, AutoIncrement = false)]
		public int idPaid;
		public decimal PaymentAmount;
		public override int? Id {
			get { return idPayment; }
			set { idPayment = value; }
		}
	}

	[Table]
	public class Product : JsonObject {
		[Primary]
		public int? idProduct;
		[Length(75)]
		[Unique("Name_UNIQUE")]
		public string ProductName;
		[Length(75)]
		public string ProductDescription;
		public decimal UnitPrice;
		[ForeignKey("VatCode")]
		public int? VatCodeId;
		[ForeignKey("Account")]
		public int AccountId;
		/// <summary>
		/// Input/display unit - these are implemented in default.js,
		/// and include D:H:M and H:M
		/// </summary>
		public int Unit;
		public override int? Id {
			get { return idProduct; }
			set { idProduct = value; }
		}
	}

	[Table]
	public class Report : JsonObject {
		[Primary]
		public int? idReport;
		[Length(75)]
		[Unique("Name_Index", 1)]
		public string ReportName;
		[Length(25)]
		public string ReportType;
		/// <summary>
		/// Json of report settings
		/// </summary>
		[Length(0)]
		public string ReportSettings;
		[Unique("Name_Index")]
		public string ReportGroup;
		public override int? Id {
			get { return idReport; }
			set { idReport = value; }
		}
	}

	[Table]
	public class Security : JsonObject {
		[Primary]
		public int? idSecurity;
		[Unique("Name_UNIQUE")]
		public string SecurityName;
		[Unique("Ticker_UNIQUE")]
		public string Ticker;
		public override int? Id {
			get { return idSecurity; }
			set { idSecurity = value; }
		}
	}

	[Table]
	public class StockTransaction : JsonObject {
		[ForeignKey("Journal")]
		[Primary(AutoIncrement = false)]
		public int? idStockTransaction;
		[ForeignKey("Account")]
		public int? ParentAccountId;
		[ForeignKey("Security")]
		public int SecurityId;
		public double Quantity;
		public double CostPer;
		public double Price;
		public override int? Id {
			get { return idStockTransaction; }
			set { idStockTransaction = value; }
		}
	}

	[Table]
	public class StockPrice : JsonObject {
		[Primary(AutoIncrement = false)]
		[ForeignKey("Security")]
		public int? SecurityId;
		[Primary(1, AutoIncrement = false)]
		public DateTime Date;
		public double Price;
	}

	[Table]
	public class Schedule : JsonObject {
		[Primary]
		public int? idSchedule;
		public DateTime ActionDate;
		/// <summary>
		/// <see cref="RepeatType"/>
		/// </summary>
		public int RepeatType;
		[DefaultValue(1)]
		public int RepeatFrequency;
		[Length(75)]
		public string Task;
		/// <summary>
		/// Url where to post data for posts, or to redirect to
		/// </summary>
		[Nullable]
		public string Url;
		/// <summary>
		/// Json of transaction to post
		/// </summary>
		[Nullable]
		[Length(0)]
		public string Parameters;
		/// <summary>
		/// True if transaction has to be posted
		/// </summary>
		public bool Post;
		public override int? Id {
			get { return idSchedule; }
			set { idSchedule = value; }
		}
	}

	[Table]
	public class Settings : CodeFirstWebFramework.Settings {
		[ForeignKey("Account")]
		public int? DefaultBankAccount;
		[Length(75)]
		public string CompanyName;
		[Length(0)]
		public string CompanyAddress;
		public string CompanyPhone;
		[Length(50)]
		public string CompanyEmail;
		public string WebSite;
		[Length(25)]
		public string VatRegistration;
		[Length(25)]
		public string CompanyNumber;
		[DefaultValue("1")]
		public int YearStartMonth;
		public int YearStartDay;
		[DefaultValue("14")]
		public int TermsDays;
		[DefaultValue("1")]
		public int NextInvoiceNumber;
		[DefaultValue("1")]
		public int NextBillNumber;
		[DefaultValue("1")]
		public int NextJournalNumber;
		[DefaultValue("smtp.gmail.com")]
		public string MailServer;
		[DefaultValue("587")]
		public int MailPort;
		[DefaultValue("1")]
		public bool MailSSL;
		[Length(50)]
		public string MailUserName;
		public string MailPassword;

		public int NextNumber(DocType docType) {
			switch (docType) {
				case DocType.Invoice:
				case DocType.CreditMemo:
					return NextInvoiceNumber;
				case DocType.Bill:
				case DocType.Credit:
					return NextBillNumber;
				case DocType.GeneralJournal:
					return NextJournalNumber;
				default:
					return 1;
			}
		}

		public void RegisterNumber(CodeFirstWebFramework.AppModule module, int? docType, int current) {
			switch (docType) {
				case (int)DocType.Invoice:
				case (int)DocType.CreditMemo:
					registerNumber(module, ref NextInvoiceNumber, current);
					break;
				case (int)DocType.Bill:
				case (int)DocType.Credit:
					registerNumber(module, ref NextBillNumber, current);
					break;
				case (int)DocType.GeneralJournal:
					registerNumber(module, ref NextJournalNumber, current);
					break;
			}
		}

		void registerNumber(CodeFirstWebFramework.AppModule module, ref int next, int current) {
			if (current >= next) {
				next = current + 1;
				write(module);
			}
		}

		public DateTime YearEnd(DateTime date) {
			date = date.Date;
			DateTime result = yearStart(date);
			if (result <= date)
				result = yearStart(result.AddMonths(13));
			return result.AddDays(-1);
		}

		public DateTime YearStart(DateTime date) {
			date = date.Date;
			DateTime result = yearStart(date);
			if (result > date)
				result = yearStart(date.AddMonths(-12));
			return result;
		}

		public DateTime QuarterStart(DateTime date) {
			DateTime result = YearStart(date);
			result = result.AddDays(1 - (int)result.Day);
			for (DateTime end = result.AddMonths(3); end < date; end = result.AddMonths(3))
				result = end;
			return result;
		}

		void write(CodeFirstWebFramework.AppModule module) {
			module.Database.Update(this);
		}

		DateTime yearStart(DateTime date) {
			int month = YearStartMonth;
			if (month == 0) month = 1;
			int day = YearStartDay;
			// First day of the month
			DateTime dayOfMonth = new DateTime(date.Year, month, 1);
			if (day > 0) {
				DayOfWeek dayOfWeek = (DayOfWeek)(day % 7);

				// Find first dayOfWeek of this month
				if (dayOfMonth.DayOfWeek > dayOfWeek) {
					dayOfMonth = dayOfMonth.AddDays(7 - (int)dayOfMonth.DayOfWeek + (int)dayOfWeek);
				} else {
					dayOfMonth = dayOfMonth.AddDays((int)dayOfWeek - (int)dayOfMonth.DayOfWeek);
				}
			}
			return dayOfMonth;
		}
	}

	[Table]
	public class VatCode : JsonObject {
		[Primary]
		public int? idVatCode;
		[Length(25)]
		[Unique("Name_UNIQUE")]
		public string Code;
		[Length(75)]
		public string VatDescription;
		public decimal Rate;
		public override int? Id {
			get { return idVatCode; }
			set { idVatCode = value; }
		}
	}

	[View(@"SELECT idDocument, DocumentMemo, DocumentTypeId, DocType, Sign,
DocumentDate, Journal.NameAddressId AS DocumentNameAddressId, Name AS DocumentName, DocumentAddress, DocumentIdentifier, 
Journal.Amount As AccountingAmount, Journal.Outstanding As AccountingOutstanding,
-Journal.Amount * DocumentType.Sign As DocumentAmount, -Journal.Outstanding * DocumentType.Sign As DocumentOutstanding,
Journal.AccountId AS DocumentAccountId, AccountName As DocumentAccountName, Journal.Cleared AS Clr,
VatJournal.Amount * DocumentType.Sign As DocumentVatAmount, VatPaid
FROM Document
JOIN DocumentType ON idDocumentType = DocumentTypeId
JOIN Journal ON Journal.DocumentId = idDocument AND Journal.JournalNum = 1
JOIN Account ON idAccount = Journal.AccountId
JOIN NameAddress ON idNameAddress = Journal.NameAddressId
LEFT JOIN Journal AS VatJournal ON VatJournal.DocumentId = idDocument AND VatJournal.AccountId = 8
")]
	public class Extended_Document : JsonObject {
		[Primary(AutoIncrement = false)]
		public int? idDocument;
		[Length(0)]
		public string DocumentMemo;
		public int DocumentTypeId;
		public string DocType;
		public int Sign;
		public DateTime DocumentDate;
		public int? DocumentNameAddressId;
		[Length(75)]
		public string DocumentName;
		[Length(0)]
		public string DocumentAddress;
		public string DocumentIdentifier;
		public decimal AccountingAmount;
		public decimal AccountingOutstanding;
		public decimal DocumentAmount;
		public decimal DocumentOutstanding;
		public int DocumentAccountId;
		[Length(75)]
		public string DocumentAccountName;
		[Length(1)]
		public string Clr;
		public decimal? DocumentVatAmount;
		public int? VatPaid;
		public override int? Id {
			get { return idDocument; }
			set { idDocument = value; }
		}
	}

	[View(@"SELECT Line.*, Product.ProductName, Product.UnitPrice, VatCode.Code, VatCode.VatDescription, 
Journal.DocumentId, Journal.JournalNum, Journal.Memo, Journal.AccountId, Account.AccountName, Account.AccountDescription
FROM Line
JOIN Product ON Product.idProduct = Line.ProductId
JOIN VatCode ON VatCode.idVatCode = Line.VatCodeId
JOIN Journal ON Journal.idJournal = Line.idLine
JOIN Account ON Account.idAccount = Journal.AccountId
")]
	public class Extended_Line : JsonObject {
		[Primary(AutoIncrement = false)]
		public int? idLine;
		public double Qty;
		public int? ProductId;
		public decimal LineAmount;
		public int? VatCodeId;
		public decimal VatRate;
		public decimal VatAmount;
		[Length(75)]
		public string ProductName;
		public decimal UnitPrice;
		[Length(25)]
		public string Code;
		[Length(75)]
		public string VatDescription;
		public int DocumentId;
		public int JournalNum;
		[Length(75)]
		public string Memo;
		public int AccountId;
		[Length(75)]
		public string AccountName;
		[Length(75)]
		public string AccountDescription;
		public override int? Id {
			get { return idLine; }
			set { idLine = value; }
		}
	}

	[View(@"SELECT Extended_Document.*, CASE DocumentAccountId 
WHEN 1 THEN -1 
WHEN 2 THEN 1
ELSE Sign
END AS VatType,
Memo,Line.VatCodeId, Line.VatRate, SUM(Line.VatAmount) VatAmount, SUM(Line.LineAmount) AS LineAmount 
FROM Extended_Document
JOIN Journal ON IdDocument = DocumentId
JOIN Line ON idLine = idJournal
WHERE DocumentTypeId IN (1, 3, 4, 6)
OR Line.VatCodeId IS NOT NULL
GROUP BY idDocument, Line.VatCodeId, Line.VatRate
")]
	public class Vat_Journal : JsonObject {
		[Primary(AutoIncrement = false)]
		public int? idDocument;
		[Length(0)]
		public string DocumentMemo;
		public int DocumentTypeId;
		public string DocType;
		public int Sign;
		public DateTime DocumentDate;
		public int? DocumentNameAddressId;
		[Length(75)]
		public string DocumentName;
		[Length(0)]
		public string DocumentAddress;
		public string DocumentIdentifier;
		public decimal AccountingAmount;
		public decimal AccountingOutstanding;
		public decimal DocumentAmount;
		public decimal DocumentOutstanding;
		public int DocumentAccountId;
		public string DocumentAccountName;
		[Length(1)]
		public string Clr;
		public decimal? DocumentVatAmount;
		public int? VatPaid;
		[Length(2)]
		public int VatType;
		[Length(75)]
		public string Memo;
		public int? VatCodeId;
		public decimal VatRate;
		public decimal? VatAmount;
		public decimal? LineAmount;
		public override int? Id {
			get { return idDocument; }
			set { idDocument = value; }
		}
	}

#pragma warning restore 0649
}

