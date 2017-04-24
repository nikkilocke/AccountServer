using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json.Linq;
using CodeFirstWebFramework;

namespace AccountServer {
	/// <summary>
	/// Import Quicken Import Format files
	/// </summary>
	public class QifImporter : FileProcessor {
		CodeFirstWebFramework.AppModule _module;
		TextReader _reader;
		string _line;
		bool _eof;
		/// <summary>
		/// First character of input line
		/// </summary>
		string _tag;
		/// <summary>
		/// Remainder of input line
		/// </summary>
		string _value;
		/// <summary>
		/// Account being processed
		/// </summary>
		int _account;
		/// <summary>
		/// Name of account being processed
		/// </summary>
		string _accountName;
		/// <summary>
		/// Transaction being built
		/// </summary>
		Transaction _transaction;
		/// <summary>
		/// Detailt line being built (last one on _transaction)
		/// </summary>
		Journal _detail;
		/// <summary>
		/// Helps identify transfers, which appear twice, once for each account, to stop them being posted twice
		/// </summary>
		List<int> _accountsProcessed;
		/// <summary>
		/// All transactions found, ready to post
		/// </summary>
		List<Transaction> _transactions;
		/// <summary>
		/// True if only inputting a statement - nothing is posted
		/// </summary>
		bool _transactionsOnly;

		/// <summary>
		/// Import a whole Qif file to the database as new accounts, transactions, etc.
		/// </summary>
		public void Import(TextReader r, CodeFirstWebFramework.AppModule module) {
			lock (this) {
				_transactionsOnly = false;
				_reader = r;
				_module = module;
				Line = 0;
				Character = 0;
				_accountsProcessed = new List<int>();
				_transactions = new List<Transaction>();
				if (!getLine()) return;
				while (!_eof) {
					switch (_line) {
						case "!Type:Cash":
						case "!Type:Bank":
							importTransactions(DocType.Cheque, DocType.Deposit);
							break;
						case "!Type:CCard":
							importTransactions(DocType.CreditCardCharge, DocType.CreditCardCredit);
							break;
						case "!Type:Invst":
							importInvestments();
							break;
						case "!Type:Oth A":
						case "!Type:Oth L":
						case "!Account":
							importAccount();
							break;
						case "!Type:Cat":
							importCategories();
							break;
						case "!Type:Security":
							importSecurity();
							break;
						case "!Type:Prices":
							importPrices();
							break;
						case "!Type:Class":
						case "!Type:Memorized":
						case "!Type:Invoice":
							// We are not interested in these
							// TODO: Process invoices
							skip();
							break;
						default:
							if (_line.StartsWith("!Option:") || _line.StartsWith("!Clear:"))
								getLine();
							else
								throw new CheckException("Unexpected input:{0}", _line);
							break;
					}
				}
				postTransactions();
			}
		}

		/// <summary>
		/// Import a bank statement, and return it (no posting is made to the database)
		/// </summary>
		public JArray ImportTransactions(TextReader r, AppModule module) {
			lock (this) {
				JArray result = new JArray();
				_transactionsOnly = true;
				_reader = r;
				_module = module;
				Line = 0;
				Character = 0;
				_accountsProcessed = new List<int>();
				_transactions = new List<Transaction>();
				if (!getLine()) return result;
				while (!_eof) {
					switch (_line) {
						case "!Type:Cash":
						case "!Type:Bank":
							importTransactions(DocType.Cheque, DocType.Deposit);
							break;
						case "!Type:CCard":
							importTransactions(DocType.CreditCardCharge, DocType.CreditCardCredit);
							break;
						default:
							skip();
							break;
					}
				}
				// Now copy wanted data to the output
				foreach (Transaction t in _transactions) {
					JObject j = new JObject();
					j["Name"] = string.IsNullOrEmpty(t.Name) ? t.DocumentMemo : t.Name;
					j["Amount"] = t.Amount;
					j["Date"] = t.DocumentDate;
					if (!string.IsNullOrEmpty(t.DocumentIdentifier)) j["Id"] = t.DocumentIdentifier;
					j["Memo"] = t.DocumentMemo;
					result.Add(j);
				}
				return result;
			}
		}

		/// <summary>
		/// For progress bar
		/// </summary>
		public int Character { get; private set; }

		/// <summary>
		/// Expected date format (if empty or null, DateTime.Parse is used)
		/// </summary>
		public string DateFormat;

		/// <summary>
		/// For error reporting
		/// </summary>
		public int Line { get; private set; }

		/// <summary>
		/// Input a wierd Quicken date
		/// </summary>
		/// <returns></returns>
		DateTime getDate() {
			// 'nn is year 2000 + nn
			_value = Regex.Replace(_value, @"'\d+$", delegate(Match m) {
				int year = int.Parse(m.Value.Substring(1));
				return "/" + (year + 2000);
			});
			return string.IsNullOrWhiteSpace(DateFormat) ? DateTime.Parse(_value) : DateTime.ParseExact(_value, DateFormat, System.Globalization.CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Get next line, split it into tag and value
		/// </summary>
		bool getLine() {
			_line = _reader.ReadLine();
			Line++;
			_eof = _line == null;
			if (!_eof) {
				_tag = _line.Substring(0, 1);
				_value = _line.Length > 1 ? _line.Substring(1) : "";
				Character += _line.Length + 2;
			}
			return !_eof;
		}

		/// <summary>
		/// Import account info for a single bank/card/security account
		/// </summary>
		void importAccount() {
			status("Importing Account");
			JObject o = new JObject();
			while (getLine()) {
				switch (_tag) {
					case "!":
						return;
					case "^":		// End of record
						if(!string.IsNullOrEmpty(o.AsString("AccountName")))
							_account = (int)_module.Database.ForeignKey("Account", o);
						getLine();	// Get next line for caller to process
						return;
					case "N":
						o["AccountName"] = _accountName = _value;
						break;
					case "D":
						o["AccountDescription"] = _value;
						break;
					case "S":	// Security stock ticker
						break;
					case "T":
						switch (_value) {
							case "CCard":
								o["AccountTypeId"] = (int)AcctType.CreditCard;
								break;
							case "Bank":
								o["AccountTypeId"] = (int)AcctType.Bank;
								break;
							case "Stock":
							case "Invst":
								o["AccountTypeId"] = (int)AcctType.Investment;
								break;
							default:
								throw new CheckException("Unexpected account type:{0}", _line);
						}
						break;
					default:
						throw new CheckException("Unexpected input:{0}", _line);
				}
			}
		}

		/// <summary>
		/// Import series of categories (accounts, in our system)
		/// </summary>
		void importCategories() {
			status("Importing Categories");
			JObject o = new JObject();
			while (getLine()) {
				switch (_tag) {
					case "!":
						return;
					case "^":		// End of record
						if(!string.IsNullOrEmpty(o.AsString("AccountName")))
							_account = (int)_module.Database.ForeignKey("Account", o);
						o = new JObject();
						break;
					case "N":
						o["AccountName"] = _accountName = _value;
						break;
					case "D":
						o["AccountDescription"] = _value;
						break;
					case "E":
						o["AccountTypeId"] = (int)AcctType.Expense;
						break;
					case "I":
						o["AccountTypeId"] = (int)AcctType.Income;
						break;
					case "B":
						break;
					default:
						throw new CheckException("Unexpected input:{0}", _line);
				}
			}
		}

		void importInvestments() {
			decimal value;
			status("Importing Investments");
			_accountsProcessed.Add(_account);
			startInvestment();
			while (getLine()) {
				switch (_tag) {
					case "!":
						return;
					case "^":	// End of record
						addInvestment();
						startInvestment();
						break;
					case "D":
						_transaction.DocumentDate = getDate();
						break;
					case "M":	// Memo
						if (_transaction.DocumentMemo == null)
							_transaction.DocumentMemo = _value;
						else if (_transaction.DocumentIdentifier == null)
							_transaction.DocumentIdentifier = _value;
						break;
					case "N":
						_transaction.DocumentIdentifier = _value;
						break;
					case "P":	// Payee
						_transaction.Name = _value;
						break;
					case "T":	// Amount
						value = decimal.Parse(_value);
						_transaction.DocumentTypeId = (int)(value < 0 ? DocType.Buy : DocType.Sell);
						_transaction.Amount = value;
						break;
					case "Y":	// Security name
						if (!string.IsNullOrEmpty(_value)) {
							_transaction.SecurityName = _value;
							_transaction.Stock.SecurityId = (int)_module.Database.ForeignKey("Security", "SecurityName", _value);
						}
						break;
					case "Q":	// Quantity (of shares)
						_transaction.Stock.Quantity = double.Parse(_value);
						break;
					case "I":	// Price
						_transaction.Stock.Price = double.Parse(_value);
						break;
					case "O":	// Commission cost
						value = decimal.Parse(_value);
						if (value != 0) {
							Journal j = new Journal();
							_transaction.Journals.Add(j);
							j.AccountId = (int)_module.Database.ForeignKey("Account", 
								"AccountName", _accountName + " fees",
								"AccountTypeId", (int)AcctType.OtherExpense);
							j.Memo = "Fees";
							j.Amount = j.Outstanding = decimal.Parse(_value);
						}
						break;
					case "L":	// Category/account
						account();
						_detail.Amount = _detail.Outstanding = -_transaction.Amount;
						break;
					case "U":	// ?? Same value as T
					case "$":	// Split amount
						break;
					case "C":	// Cleared
					case "E":	// Split memo
					case "S":	// Split category/account
					default:
						throw new CheckException("Unexpected input:{0}", _line);
				}
			}
		}

		/// <summary>
		/// Stock prices
		/// </summary>
		void importPrices() {
			status("Importing Prices");
			while (getLine()) {
				switch (_tag) {
					case "!":
						return;
					case "^":		// End of record
						getLine();	// Get next line for caller to process
						return;
					case "\"":
						string[] fields = _line.Split(',');
						string d = Utils.RemoveQuotes(fields[2]);
						StockPrice p = new StockPrice();
						p.SecurityId = (int)_module.Database.ForeignKey("Security", "Ticker", Utils.RemoveQuotes(fields[0]));
						p.Price = double.Parse(fields[1]);
						p.Date = string.IsNullOrWhiteSpace(DateFormat) ? DateTime.Parse(d) : DateTime.ParseExact(d, DateFormat, System.Globalization.CultureInfo.InvariantCulture);
						_module.Database.Update(p);;
						break;
					default:
						throw new CheckException("Unexpected input:{0}", _line);
				}
			}
		}

		void importSecurity() {
			status("Importing Security");
			JObject o = new JObject();
			o["PriceDate"] = new DateTime(1900, 1, 1);
			while (getLine()) {
				switch (_tag) {
					case "!":
						return;
					case "^":	// End of record
						if(!string.IsNullOrWhiteSpace(o.AsString("SecurityName")) && !string.IsNullOrWhiteSpace(o.AsString("Ticker")))
							_module.Database.ForeignKey("Security", o);
						getLine();	// Get next line for caller to process
						return;
					case "N":
						o["SecurityName"] = _value;
						break;
					case "S":	// Security stock ticker
						o["Ticker"] = _value;
						break;
					case "T":	// Type? (Stock)
						break;
					default:
						throw new CheckException("Unexpected input:{0}", _line);
				}
			}
		}

		void importTransactions(DocType debit, DocType credit) {
			decimal value;
			string lAccount = null;
			status("Importing Transactions");
			// For de-duplicating transfers
			_accountsProcessed.Add(_account);
			startTransaction();
			while (getLine()) {
				switch (_tag) {
					case "!":
						return;
					case "^":
						addTransaction();
						startTransaction();
						lAccount = null;
						break;
					case "C":
						_transaction.Cleared = _value;
						break;
					case "D":
						_transaction.DocumentDate = getDate();
						break;
					case "M":	// Memo
						if(_transaction.DocumentMemo == null)
							_transaction.DocumentMemo = _value;
						else if(_transaction.DocumentIdentifier == null)
							_transaction.DocumentIdentifier = _value;
						break;
					case "N":
						_transaction.DocumentIdentifier = _value;
						break;
					case "E":	// Split memo
						if (_detail.Memo != null)
							_transaction.Journals.Add(_detail = new Journal());
						_detail.Memo = _value;
						break;
					case "L":	// Category/account
						lAccount = _value;
						account();
						// Generate an initial journal line (in case there are no S split lines)
						_detail.Amount = _detail.Outstanding = -_transaction.Amount;
						break;
					case "S":	// Split category/account
						if (_value == lAccount && _transaction.Journals.Count == 1) {
							// Transaction has both L and S lines, and first S line is same as L
							// We must ignore the initial journal line we generated from the L line (in case there were no S lines)
							_detail.AccountId = 0;
							_detail.Amount = _detail.Outstanding = 0;
						}
						lAccount = null;
						account();
						break;
					case "P":	// Payee
						_transaction.Name = _value;
						break;
					case "T":	// Amount
						value = decimal.Parse(_value);
						_transaction.DocumentTypeId = (int)(value < 0 ? debit : credit);
						_transaction.Amount = value;
						break;
					case "$":	// Split amount
						value = -decimal.Parse(_value);
						if (value == 0) {
							// Value is zero - we need to remove this line and go back to previous one
							if (_transaction.Journals.Count > 1) {
								_detail = _transaction.Journals[_transaction.Journals.Count - 2];
								_transaction.Journals.RemoveAt(_transaction.Journals.Count - 1);
							} else {
								// No previous line - re-initialise this line
								_detail = new Journal();
								_transaction.Journals[0] = _detail;
							}
							break;
						}
						if (_detail.Amount != 0)
							_transaction.Journals.Add(_detail = new Journal());
						_detail.Amount = value;
						_detail.Outstanding = value;
						break;
					case "U":	// ?? Same value as T
						break;
					case "Y":	// Security name
					case "Q":	// Quantity (of shares)
					case "I":	// Price
					case "O":	// Commission cost
					default:
						throw new CheckException("Unexpected input:{0}", _line);
				}
			}
		}

		/// <summary>
		/// Have come across an account.
		/// If necessary add a new journal, and set AccountId
		/// </summary>
		void account() {
			if (string.IsNullOrEmpty(_value))
				return;
			// Transfers are shown as "[accountname]"
			string a = Regex.Replace(_value, @"^\[(.*)\]$", "$1");
			if (_detail.AccountId != 0)
				_transaction.Journals.Add(_detail = new Journal());
			_detail.AccountId = _transactionsOnly ?
				(int)_module.Database.LookupKey("Account", "AccountName", a, "AccountTypeId", (int)AcctType.Expense) : 
				(int)_module.Database.ForeignKey("Account", "AccountName", a, "AccountTypeId", (int)AcctType.Expense);
			if (a != _value)
				_transaction.DocumentTypeId = (int)DocType.Transfer;
		}

		/// <summary>
		/// Add an investment transaction to _transactions
		/// </summary>
		void addInvestment() {
			_transaction.NameAddressId = string.IsNullOrWhiteSpace(_transaction.Name) ? 1 : (int)_module.Database.ForeignKey("NameAddress", 
							"Name", _transaction.Name, 
							"Type", "O");
			switch (_transaction.DocumentIdentifier) {
				case "Buy":
					addBuy();
					return;
				case "BuyX":		// Transfer money in from another account, and use it to buy
					addTransfer(-1);
					addBuy();
					return;
				case "Sell":
					addSell();
					return;
				case "SellX":		// Sell and transfer money out to another account
					addSell();
					addTransfer(1);
					return;
				case "ReinvDiv":	// Receive a dividend and use it to buy
					addDividend();
					addBuy();
					return;
				case "Div":
					addDividend();
					return;
				case "DivX":		// Receive a dividend and transfer it to another account
					addDividend();
					addTransfer(1);
					return;
			}
			Utils.Check(_transaction.Stock.SecurityId == 0, "Unexpected stock transaction {0}", _transaction.DocumentIdentifier);
			if (_accountsProcessed.Contains(_transaction.Journals[0].AccountId)) {
				setClearedStatus(_transaction);
				return;
			}
			_transaction.Stock = null;
			Account acct = _module.Database.Get<Account>(_transaction.Journals[0].AccountId);
			_transaction.DocumentTypeId = (int)(acct.AccountTypeId == (int)AcctType.Bank || acct.AccountTypeId == (int)AcctType.CreditCard || acct.AccountTypeId == (int)AcctType.Investment ?
				DocType.Transfer : _transaction.Amount < 0 ? DocType.Cheque : DocType.Deposit);
			_transactions.Add(_transaction);
		}

		void addBuy() {
			Utils.Check(_transaction.Stock.SecurityId != 0, "Stock transaction {0} without security", _transaction.DocumentIdentifier);
			Transaction t = _transaction.Clone<Transaction>();
			t.DocumentTypeId = (int)DocType.Buy;
			t.DocumentMemo = _transaction.DocumentMemo ?? _transaction.SecurityName;
			decimal net = t.Amount;
			if (t.Journals.Count > 1)
				net -= t.Journals[1].Amount;
			t.Amount = -t.Amount;
			Journal d = t.Journals[0];
			d.AccountId = (int)_module.Database.ForeignKey("Account",
				"AccountName", _accountName + ":" + t.SecurityName,
				"AccountTypeId", (int)AcctType.Security);
			d.Amount = d.Outstanding = net;
			d.Memo = t.Stock.Quantity + " at " + t.Stock.Price;
			_transactions.Add(t);
		}

		void addSell() {
			Utils.Check(_transaction.Stock.SecurityId != 0, "Stock transaction {0} without security", _transaction.DocumentIdentifier);
			Transaction t = _transaction.Clone<Transaction>();
			t.DocumentTypeId = (int)DocType.Sell;
			t.DocumentMemo = _transaction.DocumentMemo ?? _transaction.SecurityName;
			decimal net = t.Amount;
			if (t.Journals.Count > 1)
				net += t.Journals[1].Amount;
			Journal d = t.Journals[0];
			d.AccountId = (int)_module.Database.ForeignKey("Account",
				"AccountName", _accountName + ":" + t.SecurityName,
				"AccountTypeId", (int)AcctType.Security);
			d.Amount = d.Outstanding = -net;
			d.Memo = t.Stock.Quantity + " at " + t.Stock.Price;
			t.Stock.Quantity = -t.Stock.Quantity;
			_transactions.Add(t);
		}

		void addDividend() {
			Transaction t = new Transaction();
			t.AccountId = _account;
			t.Amount = _transaction.Amount;
			t.Cleared = _transaction.Cleared;
			t.DocumentDate = _transaction.DocumentDate;
			t.DocumentIdentifier = _transaction.DocumentIdentifier;
			t.DocumentMemo = _transaction.DocumentMemo ?? _transaction.SecurityName;
			t.DocumentTypeId = (int)DocType.Deposit;
			t.Line = _transaction.Line;
			t.NameAddressId = _transaction.NameAddressId;
			t.Journals.Add(new Journal() {
				AccountId = (int)_module.Database.ForeignKey("Account", 
					"AccountName", "Dividends",
					"AccountTypeId", (int)AcctType.OtherIncome),
				Amount = -_transaction.Amount,
				Outstanding = -_transaction.Amount
			});
			_transactions.Add(t);
		}

		void addTransfer(int sign) {
			Transaction t = new Transaction();
			Account a = _module.Database.Get<Account>(_detail.AccountId);
			t.AccountId = _detail.AccountId;
			t.Amount = sign * _transaction.Amount;
			t.Cleared = _transaction.Cleared;
			t.DocumentDate = _transaction.DocumentDate;
			t.DocumentIdentifier = _transaction.DocumentIdentifier;
			t.DocumentMemo = _transaction.DocumentMemo ?? _transaction.SecurityName;
			t.DocumentTypeId = (int)(a.AccountTypeId == (int)AcctType.Bank || a.AccountTypeId == (int)AcctType.CreditCard || a.AccountTypeId == (int)AcctType.Investment ? DocType.Transfer : DocType.GeneralJournal);
			t.Line = _transaction.Line;
			t.NameAddressId = _transaction.NameAddressId;
			t.Journals.Add(new Journal() {
				AccountId = _account,
				Amount = -t.Amount,
				Outstanding = -t.Amount
			});
			if (_accountsProcessed.Contains(_transaction.Journals[0].AccountId)) {
				setClearedStatus(t);
				return;
			}
			_transactions.Add(t);
		}

		/// <summary>
		/// Add current transaction to _transactions
		/// </summary>
		void addTransaction() {
			if (!_transactionsOnly) {
				_transaction.NameAddressId = string.IsNullOrWhiteSpace(_transaction.Name) ? 1 : (int)_module.Database.ForeignKey("NameAddress",
								"Name", _transaction.Name,
								"Type", "O");
				if (_accountsProcessed.Contains(_transaction.Journals[0].AccountId)) {
					setClearedStatus(_transaction);
					return;
				}
			}
			_transactions.Add(_transaction);
		}

		/// <summary>
		/// Post all transactions in _transactions to the database
		/// </summary>
		void postTransactions() {
			_module.Batch.Record = 0;
			_module.Batch.Records = _transactions.Count;
			status("Updating database");
			foreach (Transaction t in _transactions.OrderBy(t => t.DocumentDate)) {
				_module.Batch.Record++;
				decimal total = 0;
				Line = t.Line;
				Utils.Check(t.NameAddressId > 0, "No NameAddressId");
				_module.Batch.Record++;
				_module.Database.Insert(t);
				int docid = (int)t.idDocument;
				int sign = t.DocumentTypeId == (int)DocType.Transfer || t.Amount < 0 ? 1 : -1;
				Journal j = new Journal();
				j.DocumentId = docid;
				j.JournalNum = 1;
				j.NameAddressId = t.NameAddressId;
				j.AccountId = t.AccountId;
				j.Cleared = t.Cleared;
				j.Memo = t.DocumentMemo;
				j.Amount = j.Outstanding = t.Amount;
				total += j.Amount;
				_module.Database.Insert(j);
				for (int i = 0; i < t.Journals.Count; i++) {
					Journal d = t.Journals[i];
					d.DocumentId = docid;
					d.JournalNum = i + 2;
					d.NameAddressId = t.NameAddressId;
					if(d.AccountId == 0)
						d.AccountId = (int)_module.Database.ForeignKey("Account", "AccountName", "Uncategorised", "AccountTypeId", (int)AcctType.Expense);
					total += d.Amount;
					_module.Database.Insert(d);
					Line l = new Line();
					l.idLine = d.idJournal;
					l.LineAmount = sign * d.Amount;
					_module.Database.Insert(l);
					if (i == 0 && t.Stock != null) {
						t.Stock.idStockTransaction = d.idJournal;
						t.Stock.ParentAccountId = t.AccountId;
						t.Stock.CostPer = -(double)t.Amount / t.Stock.Quantity;
						_module.Database.Insert(t.Stock);
					}
				}
				Utils.Check(total == 0, "Transaction total not zero {0}", t);
			}
		}

		/// <summary>
		/// Set cleared status on other half of a transfer
		/// </summary>
		void setClearedStatus(Transaction t) {
			if(t.Journals.Count != 1 || string.IsNullOrEmpty(t.Cleared))
				return;
			int acct = t.Journals[0].AccountId;
			if (acct > 0) {
				Account account = _module.Database.Get<Account>(acct);
				switch ((AcctType)account.AccountTypeId) {
					case AcctType.Bank:
					case AcctType.CreditCard:
					case AcctType.Investment:
						break;
					default:
						return;
				}
				Transaction other = _transactions.FirstOrDefault(o => o.AccountId == acct
					&& o.DocumentDate == t.DocumentDate
					&& o.Amount == -t.Amount
					&& o.NameAddressId == t.NameAddressId
					&& o.DocumentTypeId == (int)DocType.Transfer
					&& o.Journals.Count == 1
					&& o.Journals[0].AccountId == t.AccountId);
				if (other != null)
					other.Journals[0].Cleared = t.Cleared;
			}
		}

		/// <summary>
		/// Skip to next ! command line
		/// </summary>
		void skip() {
			status("Skipping " + _line);
			while (getLine() && _line[0] != '!')
				;
		}

		/// <summary>
		/// Set up transaction ready to process an investment
		/// </summary>
		void startInvestment() {
			_transaction = new Transaction();
			_transaction.Line = Line;
			_transaction.AccountId = _account;
			_detail = new Journal();
			_transaction.Journals.Add(_detail);
			_transaction.Stock = new StockTransaction();
		}

		/// <summary>
		/// Set up transaction ready to process
		/// </summary>
		void startTransaction() {
			_transaction = new Transaction();
			_transaction.Line = Line;
			_transaction.AccountId = _account;
			_detail = new Journal();
			_transaction.Journals.Add(_detail);
		}

		/// <summary>
		/// Set batch status (if it is a batch)
		/// </summary>
		/// <param name="s"></param>
		void status(string s) {
			if (_module.Batch != null)
				_module.Batch.Status = s;
		}

		/// <summary>
		/// Content of _transactions
		/// </summary>
		public class Transaction : Document {
			public decimal Amount;
			public string Name;
			public int NameAddressId;
			public int AccountId;
			public int Line;
			public string Cleared;
			/// <summary>
			/// The transaction lines, really - does not include the posting to AccountId
			/// </summary>
			public List<Journal> Journals = new List<Journal>();
			/// <summary>
			/// For investments only
			/// </summary>
			public StockTransaction Stock;
			public string SecurityName;
		}

	}
}
