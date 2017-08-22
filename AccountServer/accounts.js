/**
 * Account types - should correcpond to AcctType enum in C#
 * @enum {number}
 */
var AcctType = {
	Income:1,
	Expense:2,
	Security:3,
	OtherIncome:4,
	OtherExpense:5,
	FixedAsset:6,
	OtherAsset:7,
	AccountsReceivable:8,
	Bank:9,
	Investment:10,
	OtherCurrentAsset:11,
	CreditCard:12,
	AccountsPayable:13,
	OtherCurrentLiability:14,
	LongTermLiability:15,
	OtherLiability:16,
	Equity:17
};
//noinspection JSUnusedGlobalSymbols
/**
 * Fixed G/L accounts - should correspond to Acct enum in C#
 * @enum {number}
 */
var Acct = {
	SalesLedger:1,
	PurchaseLedger:2,
	OpeningBalEquity:3,
	RetainedEarnings:4,
	ShareCapital:5,
	UndepositedFunds:6,
	UninvoicedSales:7,
	VATControl:8,
	SubscriptionsIncome:20
};
/**
 * Document types - should correspond to DocType enum in C#
 * @enum {number}
 */
var DocType = {
	Invoice:1,
		Payment:2,
		CreditMemo:3,
		Bill:4,
		BillPayment:5,
		Credit:6,
		Cheque:7,
		Deposit:8,
		CreditCardCharge:9,
		CreditCardCredit:10,
		GeneralJournal:11,
		Transfer:12,
		OpeningBalance:13,
		Buy:14,
		Sell:15,
		Gain:16,
		Subscriptions:17
};

/**
 * When user clicks on an item in a list, open it
 * @param data for item - must contain idAccount and idAccountType
 * @returns {boolean} true if there was somewhere to go to
 */
function openDetail(data) {
	var url = detailUrl(data);
	if(url)
		goto(url);
	else
		return false;
}

/**
 * When user clicks on a document in a list, open it
 * @param data for item - must contain idDocument and DocumentTypeId
 * @param acct Parent account (e.g. bank account) if relevant
 * @returns {boolean} true if there was somewhere to go to
 */
function openDocument(data, acct) {
	var url = documentUrl(data);
	if(url)
		goto(url + '&acct=' + acct);
	else
		return false;
}

/**
 * Work out the url to go to when someone clicks on an item in a list
 * @param data for item - must contain idAccount and idAccountType
 * @param {number} data.idAccount
 * @param {number} data.idAccountType
 * @returns {*} [url] (or null)
 */
function detailUrl(data) {
	var url;
	if(!data || !data.idAccount)
		return;
	switch(data.idAccountType) {
		case AcctType.Bank:
		case AcctType.CreditCard:
			url = '/banking';
			break;
		case AcctType.Investment:
			url = '/investments';
			break;
		case '':
		case undefined:
		case null:
		case 0:
			return;
		default:
			url = '/accounting';
			break;
	}
	return url + '/detail.html?id=' + data.idAccount;
}

/**
 * Work out the url to go to when someone clicks on an document in a list
 * @param data for item - must contain idDocument and DocumentTypeId
 * @param {number} data.idDocument
 * @param {number} data.DocumentTypeId
 * @returns {*} [url] (or null)
 */
function documentUrl(data) {
	var s;
	if(!data)
		return;
	switch(data.DocumentTypeId) {
		case DocType.Invoice:
		case DocType.CreditMemo:
			s = '/customer/document';
			break;
		case DocType.Payment:
			s = '/customer/payment';
			break;
		case DocType.Bill:
		case DocType.Credit:
			s = '/supplier/document';
			break;
		case DocType.BillPayment:
			s = '/supplier/payment';
			break;
		case DocType.Cheque:
		case DocType.Deposit:
		case DocType.CreditCardCharge:
		case DocType.CreditCardCredit:
			s = '/banking/document';
			break;
		case DocType.GeneralJournal:
			s = '/Accounting/document';
			break;
		case DocType.Transfer:
			s = '/banking/transfer';
			break;
		case DocType.Buy:
		case DocType.Sell:
			s = '/investments/document';
			break;
		case DocType.Subscriptions:
			s = '/members/document';
			break;
		default:
			return;
	}
	return s + '.html?id=' + data.idDocument + "&type=" + data.DocumentTypeId;
}

/**
 * Memorised task repeat selection options
 */
var repeatSelectOptions = [
	{ id: 0, value: '' },
	{ id: 1, value: 'Daily' },
	{ id: 2, value: 'Weekly' },
	{ id: 3, value: 'Monthly' },
	{ id: 4, value: 'Quarterly' },
	{ id: 5, value: 'Yearly' }
];

/**
 * Return true if currently matching a statement
 * @returns {boolean}
 */
function matchingStatement() {
	return window.location.pathname == '/banking/statementmatch.html';
}

/**
 * Adjust form options if matching statements (remove deletem next, previous, save and close, save and new buttons)
 * @returns {boolean}
 */
function checkForStatementMatching(options) {
	if(matchingStatement()) {
	if(options.header !== undefined && options.detail !== undefined) {
			checkForStatementMatching(options.header);
			checkForStatementMatching(options.detail);
		}
		options.canDelete = false;
		options.saveAndClose = false;
		options.saveAndNew = false;
		if(options.data) {
			options.data.previous = false;
			options.data.next = false;
		}
	}
	return options;
}
