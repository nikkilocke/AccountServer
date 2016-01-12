var unsavedInput;	// True if an edit field has been changed, and not saved
// var testHarness;	// True if running in Firefox (used for automated tests)
var touchScreen;	// True if running on a tablet or phone
var decPoint;		// The decimal point character of this locale
// Extend auto-complete widget to cope with multiple categories
$.widget( "custom.catcomplete", $.ui.autocomplete, {
	_create: function() {
		this._super();
		this.widget().menu( "option", "items", "> :not(.ui-autocomplete-category)" );
	},
	_renderMenu: function( ul, items ) {
		var that = this,
			currentCategory = "";
		$.each( items, function( index, item ) {
			var li;
			if ( item.category != currentCategory ) {
				ul.append( "<li class='ui-autocomplete-category'>" + item.category + "</li>" );
				currentCategory = item.category;
			}
			li = that._renderItemData( ul, item );
			if ( item.category ) {
				li.attr( "aria-label", item.category + " : " + item.label );
			}
		});
	}
});

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
	VATControl:8
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
		Loss:17
};

$(function() {
//	testHarness = bowser.firefox && hasParameter('test');
	touchScreen = bowser.mobile || bowser.tablet;
	decPoint = (1.23).toLocaleString()[1];
	resize();
	$('#menuicon').click(function() {
		// Small screen user has clicked menu icon - show/hide menu
		$('#header').slideToggle();
	});
	$('body').on('click', 'button[href]', function() {
		// Buttons with hrefs act like links
		window.location = $(this).attr('href');
	});
	$('body').on('click', 'button[data-goto]', function() {
		// Buttons with data-goto act like links, but also store state to come back to
		goto($(this).attr('data-goto'));
	});
	$(window).bind('beforeunload', function () {
		// Warn user if there is unsaved data on leaving the page
		if(unsavedInput) return "There is unsaved data";
	});
	$(window).on('resize', resize);
	$(window).on('unload', function() {
		// Disable input fields & buttons on page unload
		message("Please wait...");
		$(':input').prop('disabled', true);
	});
	$('body').on('change', ':input:not(.nosave)', function() {
		// User has changed an input field - set unsavedInput (except for dataTable search field)
		if(!$(this).attr('aria-controls'))
			unsavedInput = true;
	});
	$('button[type="submit"]').click(function() {
		// Submit button will presumably save the input
		message("Please wait...");
		unsavedInput = false;
	});
	$('body').on('click', 'button.reset', function() {
		// For when an ordinary reset button won't do any calculated data
		window.location.reload();
	});
	$('body').on('click', 'button.cancel', goback);
	$('body').on('click', 'button.nextButton', function() {
		// Button to set document number field to <next>, so C# will fill it in with the next one.
		$(this).prev('input[type="text"]').val('<next>').trigger('change');
	});

	if(!touchScreen) {
		// Moving focus to a field selects the contents (except on touch screens)
		var focusInput;
		$('body').on('focus', ':input', function () {
			focusInput = this;
			$(this).select();
		}).on('mouseup', ':input', function (e) {
			if(focusInput == this) {
				focusInput = null;
				e.preventDefault();
			}
		});
	}
	var components = window.location.pathname.split('/');
	// Highlight top level menu item corresponding to current module
	$('#menu1 button[href="/' + components[1] + '/default.html"]').addClass('highlight');
	// Highlight second level menu item corresponding to current url
	$('#menu2 button').each(function() {
		var href = $(this).attr('href');
		if(href == window.location.pathname + window.location.search)
			$(this).addClass('highlight');
	});
	setTimeout(function() {
		// Once initial form creation is done:
		//  add a Back button if there isn't one
		if (/[^\/]\//.test(window.location.pathname) && $('button#Back').length == 0)
			actionButton('Back').click(goback);
		//  Focus to the first input field
		var focusField = $(':input[autofocus]:enabled:visible:first');
		if (focusField.length == 0)
			focusField = $(':input:enabled:visible:not(button):first');
		focusField.focus().select();
	}, 100);
});

/**
 * Add a goto button to menu 2
 * @param text Button text
 * @param url Url to go to
 * @returns {*|jQuery} Button
 */
function addButton(text, url) {
	return $('<button id="' + text.replace(/ /g, '') + '" data-goto="' + url + '"></button>')
		.text(text)
		.appendTo($('#menu2'));
}

/**
 * Add a link button to menu 2
 * @param text Button text
 * @param url Url to link to
 * @returns {*|jQuery} Button
 */
function jumpButton(text, url) {
	return $('<button id="' + text.replace(/ /g, '') + '" href="' + url + window.location.search + '"></button>')
		.text(text)
		.appendTo($('#menu2'));
}

/**
 * Add a button to menu 3
 * @param text Button text
 * @returns {*|jQuery} Button
 */
function actionButton(text) {
	return $('<button id="' + text.replace(/ /g, '') + '"></button>')
		.text(text)
		.appendTo($('#menu3'));
}

/**
 * Show message at top of screen
 * @param m message
 */
function message(m) {
	if(m) $('#message').text(m);
	else $('#message').html('&nbsp;');
}

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
		default:
			return;
	}
	return s + '.html?id=' + data.idDocument + "&type=" + data.DocumentTypeId;
}

/**
 * Layout the window after a resize
 */
function resize() {
	var top = $('#header').height();
	// A small screen - should match "@media screen and (min-width:700px)" in default.css
  	var auto = $(window).width() < 700;
	$('#spacer').css('height', auto ? '' : top + 'px');
	$('#body').css('height', auto ? '' : ($(window).height() - top - 16) + 'px');
}

/**
 * Parse a decimal number (up to 2 places).
 * @param n Number string
 * @returns {*} Formatted number, or n if n is null, empty or 0
 */
function parseNumber(n) {
	if(!n)
		return n;
	if(!/^[+-]?\d+(\.\d{1,2})?$/.test(n))
		throw n + ' is not a number';
	return parseFloat(n);
}

/**
 * Parse a double number (up to 4 places).
 * @param n Number string
 * @returns {*} Formatted number, or n if n is null, empty or 0
 */
function parseDouble(n) {
	if(!n)
		return n;
	if(!/^[+-]?\d+(\.\d{1,4})?$/.test(n))
		throw n + ' is not a number';
	return parseFloat(n);
}

/**
 * Parse an intefer number.
 * @param n Number string
 * @returns {*} Formatted number, or n if n is null, empty or 0
 */
function parseInteger(n) {
	if(!n)
		return n;
	if(!/^[+-]?\d+$/.test(n))
		throw n + ' is not a whole number';
	return parseInt(n);
}

/**
 * Parse a date.
 * @param {string} date
 * @returns {Date|string} Date, or argument if it is null or empty
 */
function parseDate(date) {
	if(!date)
		return date;
	try {
		return new Date(Date.parse(date));
	} catch(e) {
		return date;
	}
}

/**
 * Format a date into local format.
 * @param {string|Date} date
 * @returns {string} Formatted date, or '' if invalid
 */
function formatDate(date) {
	if(!date)
		return date || '';
	try {
		var d = Date.parse(date);
		if(isNaN(d))
			return date || '';
		return new Date(d).toLocaleDateString();
	} catch(e) {
		return date || '';
	}
}

/**
 * Format a date & time into local format.
 * @param {string} date
 * @returns {string} Formatted date, or '' if invalid
 */
function formatDateTime(date) {
	return formatDate(date);
}

/**
 * Format a decimal number to 2 places.
 * @param number
 * @returns {string}
 */
function formatNumber(number) {
	return number == null || number === '' ? '' : parseFloat(number).toFixed(2);
}

/**
 * Format a decimal number to 2 places with commas.
 * @param number
 * @returns {string}
 */
function formatNumberWithCommas(number) {
	if( number == null || number === '')
		return '';
	number = parseFloat(number).toLocaleString();
	var p = number.indexOf(decPoint);
	if(p == -1)
		return number + '.00';
	return (number + '00').substr(0, p + 3);
}

function formatWholeNumberWithCommas(number) {
	number = formatNumberWithCommas(number);
	var p = number.indexOf(decPoint);
	if(p >= 0)
		number = number.substr(0, p);
	return number;
}

/**
 * Format a decimal number to 2 places with commas, and brackets if negative.
 * @param number
 * @returns {string}
 */
function formatNumberWithBrackets(number) {
	if( number == null || number === '')
		return '';
	number = formatNumberWithCommas(number);
	if(number[0] == '-')
		number = '(' + number.substr(1) + ')';
	else
		number += "\u00a0";
	return number;
}

/**
 * Format a double number with up to 4 places (no trailing zeroes after decimal point).
 * @param {number|string} number
 * @returns {string}
 */
function formatDouble(number) {
	if( number != null && number !== '') {
		number = parseFloat(number).toFixed(4);
		if (number.indexOf('.') >= 0) {
			var zeroes = /\.?0+$/.exec(number);
			if(zeroes) {
				number = number.substr(0, number.length - zeroes[0].length)
					+ '<span class="t">' + zeroes[0] + '</span>';
			}
		}
		return number;
	}
	return '';
}

/**
 * Format an integer
 * @param number
 * @returns {*}
 */
function formatInteger(number) {
	return number == null || number === '' ? '' : parseInt(number);
}

/**
 * Split a fractional number into 2 parts (e.g. Hours and Minutes)
 * @param {number} n number
 * @param {number} m Number of second part items in 1 first part item (e.g. Mins in an Hour)
 * @returns {*[]} The whole part, and the fractional part multiplied by m
 */
function splitNumber(n, m) {
	var sign = n < 0 ? -1 : 1;
	n = Math.abs(n);
	var w = Math.floor(n);
	return [sign * w, (n - w) * m];
}

/**
 * Add leading zeroes so result is 2 characters long
 * @param n
 * @returns {string}
 */
function fillNumber(n) {
	return ('00' + n).slice(-2);
}

/**
 * Convert a fractional number to one of our supported units for display
 * @param data The number
 * @param unit
 * @returns {string}
 */
function toUnit(data, unit) {
	if(data) {
		switch (unit) {
			case 1:	// D:H:M
				var d = splitNumber(data, 8);
				var m = splitNumber(d[1], 60);
				data = d[0] + ':' + m[0] + ':' + fillNumber(m[1]);
				break;
			case 2:	// H:M
				d = splitNumber(data, 60);
				data = d[0] + ':' + fillNumber(d[1]);
				break;
			case 3:
				data = parseFloat(data).toFixed(0);
				break;
			case 4:
				data = parseFloat(data).toFixed(1);
				break;
			case 5:
				data = parseFloat(data).toFixed(2);
				break;
			case 6:
				data = parseFloat(data).toFixed(3);
				break;
			case 7:
				data = parseFloat(data).toFixed(4);
				break;
			default:
				data = parseFloat(data).toFixed(4).replace(/\.?0+$/, '');
				break;
		}
	}
	return data;
}

/**
 * Convert an input unit into a fractional number
 * @param data
 * @param unit
 * @returns {number}
 */
function fromUnit(data, unit) {
	if(data) {
		switch (unit) {
			case 0:
				data = parseDouble(data);
				break;
			case 1:	// D:H:M
				var parts = data.split(':');
				switch(parts.length) {
					case 1:
						data = parseDouble(parts[0]);
						break;
					case 2:
						data = parseDouble(parts[0]) + parseDouble(parts[1]) / 8;
						break;
					case 3:
						data = parseDouble(parts[0]) + parseDouble(parts[1]) / 8 + parseDouble(parts[2]) / 480;
						break;
					default:
						throw data + ' is not in the format D:H:M';
				}
				break;
			case 2:	// H:M
				parts = data.split(':');
				switch(parts.length) {
				case 1:
					data = parseDouble(parts[0]);
					break;
				case 2:
					data = parseDouble(parts[0]) + parseDouble(parts[1]) / 60;
					break;
				case 3:
					data = parseFloat(parts[0]) * 8 + parseDouble(parts[1]) + parseDouble(parts[2]) / 60;
					break;
				default:
					throw data + ' is not in the format (D:)H:M';
				}
				break;
			case 3:
				data = parseInteger(data);
				break;
		}
	}
	return data;
}

//noinspection JSUnusedLocalSymbols,JSUnusedLocalSymbols
/**
 * Form field types
 * Each type has the following optional members (which may be overridden in the field definition):
 * render(data, type, row, meta): Renders - see DataTable documentation
 * 		data: The data for the field
 * 		type: The render type
 * 		row: The data for the whole row
 * 		meta: Information about the field
 * draw(data, rowno, row): Renders for display
 * 		data: The data for the field
 * 		rowno: The row number
 * 		row: The data for the whole row
 * defaultContent(index, col): Html to display if there is no data yet
 * update(cell, data, rowno, row): Update the table cell with the current value of data
 * 		cell: Table cell
 * 		data: The data for the field
 * 		rowno: The row number
 * 		row: The data for the whole row
 * inputValue(field, row): Extract the input value from what the user typed in the field
 * 		field: JQuery selector of the input field
 * 		row: The data for the whole row
 * sClass: The css class
 * name: The field name
 * heading: The field heading
 * selectOptions: Array of options for selects
 *
 * If any of the above are not supplied, suitable defaults are created
 */
var Type = {
	// Display only fields
	string: {
	},
	date: {
		render: function(data, type, row, meta) {
			return colRender(data ? data.substr(0, 10) : data, type, row, meta);
		},
		draw: function(data, rowno, row) {
			return formatDate(data);
		}
	},
	dateTime: {
		draw: function(data, rowno, row) {
			return formatDateTime(data);
		}
	},
	decimal: {
		render: {
			display: formatNumberWithCommas,
			filter: formatNumber
		},
		draw: formatNumberWithCommas,
		sClass: 'n'
	},
	wholeDecimal: {
		render: {
			display: formatWholeNumberWithCommas,
			filter: formatNumber
		},
		draw: formatWholeNumberWithCommas,
		sClass: 'n'
	},
	bracket: {
		render: {
			display: formatNumberWithBrackets,
			filter: formatNumber
		},
		draw: formatNumberWithBrackets,
		sClass: 'n'
	},
	double: {
		render: {
			display: formatDouble,
			filter: formatDouble
		},
		draw: formatDouble,
		sClass: 'n'
	},
	amount: {
		render: numberRender,
		draw: function(data, rowno, row) {
			return formatNumberWithCommas(Math.abs(data));
		},
		sClass: 'n'
	},
	credit: {
		// Displays abs value only if negative
		name: 'Credit',
		heading: 'Credit',
		render: numberRender,
		draw: function(data, rowno, row) {
			if(row["Credit"] !== undefined)
				data = -row["Credit"];
			return data < 0 ? formatNumberWithCommas(-data) : '';
		},
		sClass: 'n'
	},
	debit: {
		// Displays value only if positive (or 0)
		name: 'Debit',
		heading: 'Debit',
		render: numberRender,
		draw: function(data, rowno, row) {
			if(row["Debit"] !== undefined)
				data = row["Debit"];
			return data >= 0 ? formatNumberWithCommas(data) : '';
		},
		sClass: 'n'
	},
	int: {
		render: {
			display: formatInteger,
			filter: formatInteger
		},
		draw: formatInteger,
		sClass: 'n'
	},
	email: {
		draw: function(data, rowno, row) {
			return data ? '<a href="mailto:' + data + '">' + data + '</a>' : '';
		}
	},
	checkbox: {
		defaultContent: function(index, col) {
			return '<img src="/images/untick.png" data-col="' + col.name + '" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			return '<img src="/images/' + (data ? 'tick' : 'untick') + '.png" data-col="' + this.name + '" />';
		}
	},
	select: {
		// Displays appropriate text from selectOptions according to value
		draw: function(data, rowno, row) {
			if(this.selectOptions) {
				var opt = _.find(this.selectOptions, function(o) { return o.id == data; });
				if(opt)
					data = opt.value;
			}
			return data;
		}
	},
	image: {
		defaultContent: function(index, col) {
			return '<img data-col="' + col.name + '" ' + col.attributes + '/>';
		},
		render: function(data, type, row, meta) {
			var col = meta.settings.oInit.columns[meta.col];
			return '<img src="' + data + '" id="r' + meta.row + 'c' + meta.settings.oInit.columns[meta.col].name + '" data-col="' + col.name + '" ' + col.attributes + '/>';
		}
	},
	autoComplete: {
		// Auto complete input field
		defaultContent: function(index, col, row) {
			if(col.confirmAdd) {
				// Prompt user if value doesn't already exist in selectOptions
				//noinspection JSUnusedLocalSymbols
				col.change = function(newValue, rowData, col, input) {
					var item = _.find(col.selectOptions, function (v) {
						return v.value == newValue
					});
					if (item === undefined) {
						if (confirm(col.heading + ' ' + newValue + ' not found - add')) {
							item = {
								id: 0,
								value: newValue
							};
							col.selectOptions.push(item);
						} else {
							return false;
						}
					}
				};
			}
			return '<input type="text" class="autoComplete" data-col="' + col.name + '" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			return '<input type="text" id="r' + rowno + 'c' + this.name + '" class="autoComplete" data-col="' + this.name + '" value="' + data + '" ' + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			var i = cell.find('input');
			if(i.length && i.attr('id')) {
				i.val(data);
			} else {
				cell.html('<input type="text" id="r' + rowno + 'c' + this.name + '" class="autoComplete" data-col="' + this.name + '" value="' + data + '" ' + this.attributes + '/>');
				i = cell.find('input');
			}
			if(i.hasClass('ui-autocomplete-input'))
				return;
			var self = this;
			//noinspection JSUnusedLocalSymbols
			var options = {
				source: function(req, resp) {
					var re = $.ui.autocomplete.escapeRegex(req.term);
					var matcher = new RegExp( "^" + re, "i" );
					resp(_.filter(self.selectOptions, function(o) {
						return !o.hide && matcher.test(o.value);
					}));
				},
				change: function(e) {
					$(this).trigger('change');
				}
			};
			if($.isArray(this.selectOptions) && this.selectOptions.length > 0 && this.selectOptions[0].category != null) {
				i.catcomplete(options);
			} else {
				i.autocomplete(options);
			}
		}
	},
	textInput: {
		defaultContent: function(index, col) {
			return '<input type="text" data-col="' + col.name + '" size="' + col.size + '" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			if(data == null)
				data = "";
			return '<input type="text" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + data + '" size="' + this.size + '" ' + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			colUpdate('input', cell, data, rowno, this, row);
		},
		size: 45
	},
	docIdInput: {
		// Document number - also add a "Next" button to set value to <Next>
		defaultContent: function(index, col) {
			return '<input type="text" data-col="' + col.name + '" size="' + col.size + '" ' + col.attributes + '/>';
		},
		draw: function(data, rowno,  row) {
			if(data == null)
				data = "";
			var result = '<input type="text" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + data + '" size="' + this.size + '" ' + this.attributes + '/>';
			if(row.idDocument !== undefined && !row.idDocument)
				result += '<button class="nextButton">Next</button>';
			return result;
		},
		update: function(cell, data, rowno, row) {
			colUpdate('input', cell, data, rowno, this, row);
		},
		size: 45
	},
	passwordInput: {
		defaultContent: function(index, col) {
			return '<input type="password" data-col="' + col.name + '" size="' + col.size + '" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			if(data == null)
				data = "";
			return '<input type="password" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + data + '" size="' + this.size + '" ' + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			colUpdate('input', cell, data, rowno, this, row);
		},
		size: 45
	},
	textAreaInput: {
		defaultContent: function(index, col) {
			var rows = col.rows || 6;
			var cols = col.cols || 50;
			return '<textarea rows="' + rows + '" cols="' + cols + '" data-col="' + col.name + '" ' + col.attributes + '"></textarea>';
		},
		draw: function(data, rowno, row) {
			if(data == null)
				data = "";
			var rows = this.rows || 5;
			var cols = this.cols || 50;
			return '<textarea id="r' + rowno + 'c' + this.name + '" rows="' + rows + '" cols="' + cols + '" data-col="' + this.name + '" ' + this.attributes + '>' + _.escape(data) + '</textarea>';
		},
		update: function(cell, data, rowno, row) {
			colUpdate('textarea', cell, data, rowno, this, row);
		}
	},
	dateInput: {
		defaultContent: function(index, col) {
			return '<input type="date" data-col="' + col.name + '" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			data = data ? data.substr(0, 10) : '';
			return '<input type="date" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + data + '" ' + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			data = data ? data.substr(0, 10) : '';
			colUpdate('input', cell, data, rowno, this, row);
		}
	},
	decimalInput: {
		// 2 dec places
		defaultContent: function(index, col) {
			return '<input type="number" step="0.01" data-col="' + col.name + '"value="0.00" ' + col.attributes + '/>';
		},
		draw: function(data, rowno,  row) {
			return '<input type="number" step="0.01" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + formatNumber(data) + '" ' + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			var i = cell.find('input');
			if(i.length && i.attr('id')) {
				i.val(formatNumber(data));
			} else {
				cell.html(this.draw(data, rowno, row));
			}
		},
		inputValue: function(field, row) {
			return parseNumber($(field).val());
		},
		sClass: 'ni'
	},
	doubleInput: {
		// Up to 4 dec places
		defaultContent: function(index, col) {
			return '<input type="number" data-col="' + col.name + '"value="0" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			return '<input type="number" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + toUnit(data, row.Unit) + '" ' + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			var i = cell.find('input');
			if(i.length && i.attr('id')) {
				i.val(toUnit(data, row.Unit));
			} else {
				cell.html(this.draw(data, rowno, row));
			}
		},
		inputValue: function(field, row) {
			return fromUnit($(field).val(), row.Unit);
		},
		attributes: 'size="7"',
		sClass: 'ni'
	},
	creditInput: {
		name: 'Credit',
		heading: 'Credit',
		defaultContent: function(index, col) {
			return '<input type="number" step="0.01" data-col="' + col.name + '"value="0.00" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			data = data < 0 ? formatNumber(-data) : '';
			return '<input type="number" step="0.01" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + formatNumber(data) + '" ' + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			var i = cell.find('input');
			if(i.length && i.attr('id')) {
				i.val(data < 0 ? formatNumber(-data) : '');
			} else {
				cell.html(this.draw(data, rowno, row));
			}
		},
		inputValue: function(field, row) {
			return parseNumber($(field).val()) * -1;
		},
		sClass: 'ni'
	},
	debitInput: {
		name: 'Debit',
		heading: 'Debit',
		defaultContent: function(index, col) {
			return '<input type="number" step="0.01" data-col="' + col.name + '"value="0.00" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			data = data >= 0 ? formatNumber(data) : '';
			return '<input type="number" step="0.01" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + formatNumber(data) + '" ' + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			var i = cell.find('input');
			if(i.length && i.attr('id')) {
				i.val(data >= 0 ? formatNumber(data) : '');
			} else {
				cell.html(this.draw(data, rowno, row));
			}
		},
		inputValue: function(field, row) {
			return parseNumber($(field).val());
		},
		sClass: 'ni'
	},
	intInput: {
		defaultContent: function(index, col) {
			return '<input type="number" step="1" data-col="' + col.name + '"value="0" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			return '<input type="number" step="1" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + formatInteger(data) + '" ' + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			var i = cell.find('input');
			if(i.length && i.attr('id')) {
				i.val(formatInteger(data));
			} else {
				cell.html(this.draw(data, rowno, row));
			}
		},
		inputValue: function(field, row) {
			return parseInteger($(field).val());
		},
		sClass: 'ni'
	},
	checkboxInput: {
		defaultContent: function(index, col) {
			return '<input type="checkbox" data-col="' + col.name + '" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			return '<input type="checkbox" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '"' + (data ? ' checked' : '') + ' ' + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			var i = cell.find('input');
			if(i.length && i.attr('id')) {
				i.prop('checked', data ? true : false);
			} else {
				cell.html(this.draw(data, rowno, row));
			}
		},
		inputValue: function(field, row) {
			return $(field).prop('checked') ? 1 : 0;
		}
	},
	imageInput: {
		// Image file, with auto upload
		defaultContent: function(index, col) {
			return '<img data-col="' + col.name + '" ' + col.attributes + '/><br/><input type="file" class="autoUpload" data-col="' + col.name + '"/>';
		},
		draw: function(data, rowno, row) {
			if(data == null)
				data = '';
			return '<img src="' + data + '" data-col="' + this.name + '" ' + this.attributes + '/><br/><input type="file" class="autoUpload" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '"' + (this.multiple ? ' multiple="multiple"' : '') + ' />';
		},
		update: function(cell, data, rowno, row) {
			if(data == null)
				data = '';
			var i = cell.find('img');
			if(i.length && i.attr('id')) {
				i.prop('src', data);
			} else {
				cell.html(this.draw(data, rowno, row));
			}
		}
	},
	radioInput: {
		// Radio buttons from select options
		defaultContent: function(index, col) {
			return '<input type="radio" data-col="' + col.name + '" ' + col.attributes + ' value="0" />';
		},
		draw: function(data, rowno, row) {
			var select = '<label><input type="radio" id="r' + rowno + 'c' + this.name + '" name="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" ' + this.attributes + ' value="0" />Other</label>';
			var self = this;
			if(this.selectOptions) {
				_.each(this.selectOptions, function(o) {
					select += ' <label><input type="radio" name="r' + rowno + 'c' + self.name + '" data-col="' + self.name + '" ' + self.attributes + ' value="' + o.id
						+ (o.id == data ? ' checked="checked"' : '') + '" />' + _.escape(o.value) + '</label>';
				});
			}
			return select;
		},
		update: function(cell, data, rowno, row) {
			if(cell.find('input#r' + rowno + 'c' + this.name).length == 0) {
				cell.html(this.draw(data, rowno, row));
			} else {
				var i = cell.find('input[type=radio][value=' + data + ']');
				if (i.length)
					i.prop('checked', true);
			}
		},
		inputValue: function(field, row) {
			return field.value;
		}

	},
	textAreaField: {
		defaultContent: function(index, col) {
			var rows = col.rows || 6;
			var cols = col.cols || 50;
			return '<textarea rows="' + rows + '" cols="' + cols + '" data-col="' + col.name + '" ' + col.attributes + ' disabled="disabled""></textarea>';
		},
		draw: function(data, rowno, row) {
			if(data == null)
				data = "";
			var rows = this.rows || 5;
			var cols = this.cols || 50;
			return '<textarea id="r' + rowno + 'c' + this.name + '" rows="' + rows + '" cols="' + cols + '" data-col="' + this.name + '" ' + this.attributes + ' disabled="disabled">' + _.escape(data) + '</textarea>';
		},
		update: function(cell, data, rowno, row) {
			colUpdate('textarea', cell, data, rowno, this, row);
		}
	},
	decimalField: {
		defaultContent: function(index, col) {
			return '<input type="number" step="0.01" value="0.00" data-col="' + col.name + '" disabled ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			return '<input type="number" step="0.01" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + formatNumber(data) + '" disabled ' + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			var i = cell.find('input');
			if(i.length && i.attr('id')) {
				i.val(formatNumber(data));
			} else {
				cell.html(this.draw(data, rowno, row));
			}
		},
		sClass: 'ni'
	},
	doubleField: {
		defaultContent: function(index, col) {
			return '<input type="number" value="0" data-col="' + col.name + '" disabled ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			return '<input type="number" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + data + '" disabled ' + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			colUpdate('input', cell, data, rowno, this, row);
		},
		sClass: 'ni'
	},
	selectInput: {
		defaultContent: function(index, col) {
			return '<select data-col="' + col.name + '" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			var select = '<select id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" ' + this.attributes + '/>';
			if(this.selectOptions) {
				var jselect = $(select);
				addOptionsToSelect(jselect, this.selectOptions, data, this);
				select = $('<div />').append(jselect).html();
				select = select.replace(' value="' + data + '"', ' value="' + data + '" selected');
			}
			return select;
		},
		update: function(cell, data, rowno, row) {
			colUpdate('select', cell, data, rowno, this, row);
		}
	},
	selectFilter: {
		// Report filter
		defaultContent: function(index, col) {
			return '<select data-col="' + col.name + '" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			var select = '<select id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" />';
			if(this.selectOptions) {
				var jselect = $(select);
				addOptionsToSelect(jselect, this.selectOptions, data, this);
				select = $('<div />').append(jselect).html();
				select = select.replace(' value="' + data + '"', ' value="' + data + '" selected');
			}
			return select;
		},
		update: function(cell, data, rowno, row) {
			colUpdate('select', cell, data, rowno, this, row);
		}
	},
	dateFilter: {
		// Report date filter
		defaultContent: function(index, col) {
			return '<select data-col="' + col.name + '" ' + col.attributes + '/> <nobr><input type="date" disabled data-col="' + col.name + '" ' + col.attributes + '/> - <input type="date" disabled data-col="' + col.name + '" ' + col.attributes + '/></nobr>';
		},
		draw: function(data, rowno, row) {
			var range = data.range || 4;
			var disabled = range == 12 ? '' : 'disabled ';
			var start = data.start ? data.start.substr(0, 10) : '';
			var end = data.end ? data.end.substr(0, 10) : '';
			var select = '<select id="r' + rowno + 'c' + this.name + 'r" data-col="' + this.name + '" >'
				+ '';
			var jselect = $(select);
			addOptionsToSelect(jselect, dateSelectOptions, range);
			select = $('<div />').append(jselect).html();
			select = select.replace(' value="' + range + '"', ' value="' + range + '" selected');
			return select + ' <nobr><input type="date" id="r' + rowno + 'c' + this.name + 's" data-col="' + this.name + '" value="' + start + '" ' + disabled + this.attributes + '/> - <input type="date" id="r' + rowno + 'c' + this.name + 'e" data-col="' + this.name + '" value="' + end + '" ' + disabled + this.attributes + '/></nobr>';
		},
		update: function(cell, data, rowno, row) {
			var i = cell.find('select');
			var range = data.range || 4;
			var start = data.start ? data.start.substr(0, 10) : '';
			var end = data.end ? data.end.substr(0, 10) : '';
			if(i.length && i.attr('id')) {
				i.val(range);
				i = cell.find('input');
				i.prop('disabled', range != 12);
				$(i[0]).val(start);
				$(i[1]).val(end);
			} else {
				cell.html(this.draw(data, rowno, row));
			}
		},
		inputValue: function(field, row) {
			var cell = $(field).closest('td');
			var range = cell.find('select').val();
			cell.find('input').prop('disabled', range != 12);
			return {
				range: range,
				start: cell.find('input:first').val(),
				end: cell.find('input:last').val()
			};
		}
	},
	multiSelectFilter: {
		// Report multi select filter
		defaultContent: function(index, col) {
			return '<select multiple data-col="' + col.name + '" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			var select = '<select id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" multiple />';
			if(this.selectOptions) {
				var jselect = $(select);
				addOptionsToSelect(jselect, this.selectOptions, data, this);
				select = $('<div />').append(jselect).html();
				_.each(data, function(d) {
					select = select.replace(' value="' + d + '"', ' value="' + d + '" selected');
				});
			}
			return select;
		},
		update: function(cell, data, rowno, row) {
			var i = cell.find('select');
			if(i.length && i.attr('id')) {
				i.val(data);
			} else {
				cell.html(this.draw(data, rowno, row));
				i = cell.find('select');
			}
			if(i.css('display') != 'none')
				i.multiselect({
					selectedList: 2,
					uncheckAllText: 'No filter',
					noneSelectedText: 'No filter'
				});
		}
	},
	decimalFilter: {
		// Report decimal filter
		defaultContent: function(index, col) {
			return '<select data-col="' + col.name + '" ' + col.attributes + '/> <input type="number" step="0.01" data-col="' + col.name + '"value="0.00" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			var comparison = data.comparison || 0;
			var disabled = comparison > 2 ? '' : 'disabled ';
			var value = data.value || 0;
			var select = '<select id="r' + rowno + 'c' + this.name + 'r" data-col="' + this.name + '" >'
				+ '';
			var jselect = $(select);
			addOptionsToSelect(jselect, decimalSelectOptions, comparison);
			select = $('<div />').append(jselect).html();
			select = select.replace(' value="' + comparison + '"', ' value="' + comparison + '" selected');
			return select + '<input type="number" step="0.01" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + formatNumber(value) + '" ' + disabled + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			var i = cell.find('select');
			var comparison = data.comparison || 0;
			var value = data.value || 0;
			if(i.length && i.attr('id')) {
				i.val(comparison);
				i = cell.find('input');
				i.prop('disabled', comparison <= 2);
				i.val(value);
			} else {
				cell.html(this.draw(data, rowno, row));
			}
		},
		inputValue: function(field, row) {
			var cell = $(field).closest('td');
			var comparison = cell.find('select').val();
			cell.find('input').prop('disabled', comparison <= 2);
			return {
				comparison: comparison,
				value: parseNumber(cell.find('input').val())
			};
		}
	},
	doubleFilter: {
		// Report double (up tp 4 places) filter
		defaultContent: function(index, col) {
			return '<select data-col="' + col.name + '" ' + col.attributes + '/> <input type="number" data-col="' + col.name + '"value="0" ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			var comparison = data.comparison || 0;
			var disabled = comparison > 2 ? '' : 'disabled ';
			var value = data.value || 0;
			var select = '<select id="r' + rowno + 'c' + this.name + 'r" data-col="' + this.name + '" >'
				+ '';
			var jselect = $(select);
			addOptionsToSelect(jselect, decimalSelectOptions, comparison);
			select = $('<div />').append(jselect).html();
			select = select.replace(' value="' + comparison + '"', ' value="' + comparison + '" selected');
			return select + '<input type="number" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + value + '" ' + disabled + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			var i = cell.find('select');
			var comparison = data.comparison || 0;
			var value = data.value || 0;
			if(i.length && i.attr('id')) {
				i.val(comparison);
				i = cell.find('input');
				i.prop('disabled', comparison <= 2);
				i.val(value);
			} else {
				cell.html(this.draw(data, rowno, row));
			}
		},
		inputValue: function(field, row) {
			var cell = $(field).closest('td');
			var comparison = cell.find('select').val();
			cell.find('input').prop('disabled', comparison <= 2);
			return {
				comparison: comparison,
				value: parseDouble(cell.find('input').val())
			};
		}
	},
	stringFilter: {
		// Report string filter
		defaultContent: function(index, col) {
			return '<select data-col="' + col.name + '" ' + col.attributes + '/> <input type="text" data-col="' + col.name + ' ' + col.attributes + '/>';
		},
		draw: function(data, rowno, row) {
			var comparison = data.comparison || 0;
			var disabled = comparison > 2 ? '' : 'disabled ';
			var value = data.value || '';
			var select = '<select id="r' + rowno + 'c' + this.name + 'r" data-col="' + this.name + '" >'
				+ '';
			var jselect = $(select);
			addOptionsToSelect(jselect, stringSelectOptions, comparison);
			select = $('<div />').append(jselect).html();
			select = select.replace(' value="' + comparison + '"', ' value="' + comparison + '" selected');
			return select + '<input type="text" id="r' + rowno + 'c' + this.name + '" data-col="' + this.name + '" value="' + value + '" ' + disabled + this.attributes + '/>';
		},
		update: function(cell, data, rowno, row) {
			var i = cell.find('select');
			var comparison = data.comparison || 0;
			var value = data.value || '';
			if(i.length && i.attr('id')) {
				i.val(comparison);
				i = cell.find('input');
				i.prop('disabled', comparison <= 2);
				i.val(value);
			} else {
				cell.html(this.draw(data, rowno, row));
			}
		},
		inputValue: function(field, row) {
			var cell = $(field).closest('td');
			var comparison = cell.find('select').val();
			cell.find('input').prop('disabled', comparison <= 2);
			return {
				comparison: comparison,
				value: cell.find('input').val()
			};
		}
	}
};

/**
 * Report date selection options
 */
var dateSelectOptions = [
	{ id: 1, value: 'All' },
	{ id: 2, value: 'Today' },
	{ id: 3, value: 'This Week' },
	{ id: 4, value: 'This Month' },
	{ id: 5, value: 'This Quarter' },
	{ id: 6, value: 'This Year' },
	{ id: 7, value: 'Yesterday' },
	{ id: 8, value: 'Last Week' },
	{ id: 9, value: 'Last Month' },
	{ id: 10, value: 'Last Quarter' },
	{ id: 11, value: 'Last Year' },
	{ id: 12, value: 'Custom' }

];

/**
 * Report decimal selection options
 */
var decimalSelectOptions = [
	{ id: 0, value: 'All' },
	{ id: 1, value: 'Zero' },
	{ id: 2, value: 'Non-zero' },
	{ id: 3, value: 'Less than or equal' },
	{ id: 4, value: 'Greater than or equal' },
	{ id: 5, value: 'Equal' },
	{ id: 6, value: 'Not equal' }

];

/**
 * Report string selection options
 */
var stringSelectOptions = [
	{ id: 0, value: 'All' },
	{ id: 1, value: 'Empty' },
	{ id: 2, value: 'Non-empty' },
	{ id: 3, value: 'Equal' },
	{ id: 4, value: 'Contains' },
	{ id: 5, value: 'Starts with' },
	{ id: 6, value: 'Ends with' }
];

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
 * Units
 */
var unitOptions = [
	{ id: 0, value: 'decimal', unit: '' },
	{ id: 1, value: 'days', unit: 'D:H:M' },
	{ id: 2, value: 'hours', unit: 'H:M' },
	{ id: 3, value: 'units', unit: '' },
	{ id: 4, value: '1 dp', unit: '' },
	{ id: 5, value: '2 dp', unit: '' },
	{ id: 6, value: '3 dp', unit: '' },
	{ id: 7, value: '4 dp', unit: '' }
];

/**
 * Descriptions of units to show in unit column
 */
var unitDisplay = [
	{ id: 0, value: '' },
	{ id: 1, value: 'D:H:M' },
	{ id: 2, value: 'H:M' },
	{ id: 3, value: '' }
];

var DataTable;
/**
 * Make a DataTable
 * Column options are:
 * {string} [prefix]dataItemName[/heading] (prefix:#=decimal, /=date, @=email)
 * or
 * {*} [type] Type.* - sets defaults for column options
 * {string} data item name
 * {string} [heading]
 * {boolean|*}	nonZero true to suppress zero items, with button to reveal, false opposite, or:
 * 	{boolean} [hide] true to suppress zero items, with button to reveal, false opposite
 * 	{string} [heading] to use in button text (col.heading)
 * 	{string} [zeroText] prompt for button (Show all <heading>)
 * 	{string} [nonZeroText] prompt for button (Only non-zero <heading>)
 * @param {string} selector
 * @param options
 * @param {string} [options.table] Name of SQL table
 * @param {string} [options.idName] Name of id field in table (id<table>)
 * @param {string|function} [options.select] Url to go to or function to call when a row is clicked
 * @param {string|*} [options.ajax] Ajax settings, or string url (current url + 'Listing')
 * @param {number?} [options.iDisplayLength] Number of items to display per screen
 * @param {Array} [options.order] Initial sort order (see jquery.datatables)
 * @param {boolean?} [options.stateSave] Whether to save state
 * @param {function} [options.stateSaveCallback] Callback to save state
 * @param {function} [options.stateLoadCallback] Callback to load saved state
 * @param {*} [options.data] Existing data to display
 * @param {Array} options.columns
 * @param {function} [options.validate] Callback to validate data
 * @returns {*}
 */
function makeDataTable(selector, options) {
	var tableName = myOption('table', options);
	var idName = myOption('id', options, 'id' + tableName);
	var selectUrl = myOption('select', options);
	// Show All options
	var nzColumns = [];
	var dtParam = getParameter('dt');
	var nzList = dtParam === null || dtParam === '' ? [] : dtParam.split(',');
	// Default number of items to display depends on screen size
	if(options.iDisplayLength === undefined && $(window).height() >= 1200)
		options.iDisplayLength = 25;
	if (typeof(selectUrl) == 'string') {
		// Turn into a function that goes to url, adding id of current row as parameter
		var s = selectUrl;
		selectUrl = function (row) {
			goto(s + '?id=' + row[idName]);
		}
	}
	// If no data or data url supplied, use an Ajax call to this method + "Listing"
	if (options.data === undefined)
		_setAjaxObject(options, 'Listing', '');
	$(selector).addClass('form');
	// Make sure there is a table heading
	var heading = $(selector).find('thead');
	if (heading.length == 0) heading = $('<thead></thead>').appendTo($(selector));
	heading = $('<tr></tr>').appendTo(heading);
	var columns = {}
	_.each(options.columns, function (col, index) {
		// Set up the column - add any missing functions, etc.
		options.columns[index] = col = _setColObject(col, tableName, index);
		var title = myOption('heading', col);
		$('<th></th>').appendTo(heading).text(title).addClass(col.sClass).attr('title', col.hint);
		// Add to columns hash by name
		columns[col.name] = col;
		// "Show All" option?
		var nz = myOption('nonZero', col);
		if (nz != undefined) {
			if (typeof(nz) == 'boolean') nz = {hide: nz};
			if (nz.hide === undefined) nz.hide = true;
			if (nzList.length)
				nz.hide = nzList.shift() == 1;
			nz.col = col;
			if (nz.heading === undefined) nz.heading = title;
			nzColumns.push(nz);
		}
		col.index = index;
	});
	if (options.order == null)
		options.order = [];
	if (options.stateSave === undefined) {
		// By default, save and restore table UI state
		options.stateSave = true;
		options.stateLoadCallback = function (settings) {
			try {
				if (dtParam !== null)
					return JSON.parse(sessionStorage.getItem(
						'DataTables_' + settings.sInstance + '_' + location.pathname + '_' + getParameter('id')
					));
			} catch (e) {
			}
		};
		options.stateSaveCallback = function (settings, data) {
			try {
				sessionStorage.setItem(
					'DataTables_' + settings.sInstance + '_' + location.pathname + '_' + getParameter('id'),
					JSON.stringify(data)
				);
			} catch (e) {
			}
		};
	}
	if (typeof(selectUrl) == 'function')
		$(selector).addClass('noselect');
	var table = $(selector).dataTable(options);

	// Attach mouse handlers to each row
	if (typeof(selectUrl) == 'function') {
		selectClick(selector, function () {
			return selectUrl.call(this, table.rowData($(this)))
		});
	} else {
		selectClick(selector, null);
	}
	if(options.download || options.download === undefined) {
		actionButton('Download').click(function () {
			var i = table.api().page.info();
			table.api().page.len(-1);
			table.api().draw();
			var data = tableData(selector);
			table.api().page.len(i.length);
			table.api().draw();
			download(this, data);
		});
	}
	// "Show All" functionality
	_.each(nzColumns, function(nz) {
		var zText = nz.zeroText || ('Show all ' + nz.heading);
		var nzText = nz.nonZeroText || ('Only non-zero ' + nz.heading);
		//noinspection JSUnusedLocalSymbols
		$('<button id="nz' + nz.col.name + '" data-nz="' + nz.hide + '"></button>').insertBefore($(selector))
			.html(nz.hide ? nzText : zText)
			.click(function(e) {
				nz.hide = !nz.hide;
				$(this).attr('data-nz', nz.hide);
				$(this).html(nz.hide ? nzText : zText);
				table.api().draw();
			});
		$.fn.dataTable.ext.search.push(
			function(settings, dataArray, dataIndex, data) {
				return !nz.hide || !/^([0\.]*|true)$/.test(data[nz.col.data]);
			}
		);
		if(nz.hide)
			table.api().draw(false);
	});
	// Attach event handler to input fields
	$('body').off('change', selector + ' :input');
	$('body').on('change', selector + ' :input', function() {
		$('button#Back').text('Cancel');
		var col = table.fields[$(this).attr('data-col')];
		if(col) {
			var row = table.row(this);
			var data = row.data();
			var val;
			try {
				//noinspection JSCheckFunctionSignatures
				val = col.inputValue(this, row);
			} catch(e) {
				message(col.heading + ':' + e);
				$(this).focus();
				return;
			}
			setTimeout(function() {
				if ($(selector).triggerHandler('changed.field', [val, data, col, this]) != false) {
					data[col.data] = val;
				}
			}, 10);
		}
	});
	/**
	 * Return the tr row of item clicked on
	 * @param item
	 * @returns {*}
	 */
	table.row = function(item) {
		item = $(item);
		if(item.attr['tagName'] != 'tr') item = item.closest('tr');
		return table.api().row(item);
	};
	/**
	 * Refresh the row containing item without losing the focus
	 * @param item
	 */
	table.refreshRow = function(item) {
		var focus = $(':focus');
		var col = focus.closest('td').index();
		var row = focus.closest('tr').index();
		var refocus = focus.closest('table')[0] == table[0];
		table.row(item).invalidate().draw(false);
		if(refocus)
			table.find('tbody tr:eq(' + row + ') td:eq(' + col + ') :input').focus();
	};
	/**
	 * Refresh the whole table without losing the focus
 	 */
	table.refresh = function() {
		var focus = $(':focus');
		var col = focus.closest('td').index();
		var row = focus.closest('tr').index();
		var refocus = focus.closest('table')[0] == table[0];
		table.api().draw(false);
		if(refocus)
			table.find('tbody tr:eq(' + row + ') td:eq(' + col + ') :input').focus();
	};
	/**
	 * Return the data for the row containing r
	 * @param r
	 * @returns {*}
	 */
	table.rowData = function(r) {
		return table.row(r).data();
	};
	/**
	 * When data has arrived, update the table
	 * @param data
	 */
	table.dataReady = function(data) {
		table.api().clear();
		table.api().rows.add(data);
		table.api().draw();
	};
	table.fields = columns;
	DataTable = table.api();
	return table;
}

/**
 * Make a form to edit a single record and post it back
 * Column options are:
 * {string} [prefix]dataItemName[/heading] (prefix:#=decimal, /=date, @=email)
 * or
 * {*} [type] Type.* - sets defaults for column options
 * {string} data item name
 * {string} [heading]
 * {boolean|*}	nonZero true to suppress zero items, with button to reveal, false opposite, or:
 * 	{boolean} [hide] true to suppress zero items, with button to reveal, false opposite
 * 	{string} [heading] to use in button text (col.heading)
 * 	{string} [zeroText] prompt for button (Show all <heading>)
 * 	{string} [nonZeroText] prompt for button (Only non-zero <heading>)
 * @param {string} selector
 * @param options
 * @param {string} [options.table] Name of SQL table
 * @param {string} [options.idName] Name of id field in table (id<table>)
 * @param {string|function} [options.select] Url to go to or function to call when a row is clicked
 * @param {string|*} [options.ajax] Ajax settings, or string url (current url + 'Listing')
 * @param {boolean} [options.dialog] Show form as a dialog when Edit button is pushed
 * @param {string} [options.submitText} Text to use for Save buttons (default "Save")
 * @param {boolean} [options.saveAndClose} Include Save and Close button (default true)
 * @param {boolean} [options.saveAndNew} Include Save and New button (default false)
 * @param {*} [options.data] Existing data to display
 * @param {Array} options.columns
 * @param {function} [options.validate] Callback to validate data
 * @returns {*}
 */
function makeForm(selector, options) {
	var tableName = myOption('table', options);
	var canDelete = myOption('canDelete', options);
	var submitUrl = myOption('submit', options);
	var deleteButton;
	if(submitUrl === undefined) {
		submitUrl = defaultUrl('Post');
	}
	if(typeof(submitUrl) == 'string') {
		// Turn url into a function that validates and posts
		var submitHref = submitUrl;
		/**
		 * Submit method attached to button
		 * @param button The button pushed
		 */
		submitUrl = function(button) {
			var hdg = null;
			try {
				// Check each input value is valid
				_.each(options.columns, function (col) {
					if(col.inputValue) {
						hdg = col.heading;
						col.inputValue(col.cell.find('#r0c' + col.name), result.data);
					}
				});
			} catch(e) {
				message(hdg + ':' + e);
				return;
			}
			if(options.validate) {
				var msg = options.validate();
				message(msg);
				if(msg) return;
			}
			postJson(submitHref, result.data, function(d) {
				$('button#Back').text('Back');
				if($(button).hasClass('goback')) {
					goback();	// Save and Close
				} else if($(button).hasClass('new')) {
					window.location = urlParameter('id', 0);	// Save and New
				} else if(tableName && d.id) {
					window.location = urlParameter('id', d.id);	// Redisplay saved record
				}
			});
		}
	}
	var deleteUrl = canDelete && !matchingStatement() ? myOption('delete', options) : null;
	if(deleteUrl === undefined) {
		deleteUrl = defaultUrl('Delete');
	}
	if(typeof(deleteUrl) == 'string') {
		var deleteHref = deleteUrl;
		//noinspection JSUnusedLocalSymbols
		deleteUrl = function(button) {
			postJson(deleteHref, result.data, goback);
		}
	}
	$(selector).addClass('form');
	_setAjaxObject(options, 'Data', '');
	var row;
	var columns = {};
	_.each(options.columns, function(col, index) {
		options.columns[index] = col = _setColObject(col, tableName, index);
		if(!row || !col.sameRow)
			row = $('<tr></tr>').appendTo($(selector));
		$('<th></th>').appendTo(row).text(col.heading).attr('title', col.hint);
		col.cell = $('<td></td>').appendTo(row).html(col.defaultContent);
		if(col.colspan)
			col.cell.attr('colspan', col.colspan);
		columns[col.name] = col;
		col.index = index;
	});
	// Attach event handler to input fields
	$('body').off('change', selector + ' :input');
	$('body').on('change', selector + ' :input', function(/** this: jElement */) {
		$('button#Back').text('Cancel');
		var col = result.fields[$(this).attr('data-col')];
		if(col) {
			var val;
			try {
				//noinspection JSCheckFunctionSignatures
				val = col.inputValue(this, result.data);
			} catch(e) {
				message(col.heading + ':' + e);
				$(this).focus();
				return;
			}
			if(col.change) {
				var nval = col.change(val, result.data, col, this);
				if(val === false) {
					$(this).val(result.data[col.data])
						.focus();
					return;
				} else if(nval !== undefined && nval !== null)
					val = nval;
			}
			if($(selector).triggerHandler('changed.field', [val, result.data, col, this]) !== false) {
				if(this.type == 'file') {
					var img = $(this).prev('img');
					var submitHref = defaultUrl('Upload');
					var d = new FormData();
					for(var f = 0; f < this.files.length; f++)
						d.append('file' + (f || ''), this.files[f]);
					d.append('json', JSON.stringify(result.data));
					postFormData(submitHref, d, function(d) {
						if(tableName && d.id)
							window.location = urlParameter('id', d.id);
					});
				} else {
					result.data[col.data] = val;
					_.each(options.columns, function (c) {
						if (c.data == col.data) {
							c.update(c.cell, val, 0, result.data);
						}
					});
				}
			}
		}
	});
	var result = $(selector);

	/**
	 * Redraw form fields
	 */
	function draw() {
		//noinspection JSUnusedLocalSymbols
		_.each(options.columns, function (col, index) {
			var colData = result.data[col.data];
			col.update(col.cell, colData, 0, result.data);
		});
	}
	var drawn = false;

	/**
	 * Draw form when data arrives
	 * @param d
	 */
	function dataReady(d) {
		result.data = d;
		if(deleteButton && !d['id' + tableName])
			deleteButton.remove();
		draw();
		// Only do this bit once
		if(drawn)
			return;
		drawn = true;
		if(submitUrl) {
			if(options.dialog) {
				// Wrap form in a dialog, called by Edit button
				result.wrap('<div id="dialog"></div>');
				result.parent().dialog({
					autoOpen: false,
					modal: true,
					height: Math.min(result.height() + 200, $(window).height() * 0.9),
					width: Math.min(result.width() + 20, $(window).width()),
					buttons: {
						Ok: {
							id: 'Ok',
							text: 'Ok',
							click: function() {
								submitUrl(this);
								$(this).dialog("close");
							}
						},
						Cancel: {
							id: 'Cancel',
							text: 'Cancel',
							click: function () {
								$(this).dialog("close");
							}
						}
					}
				});
				actionButton('Edit')
					.click(function (e) {
						result.parent().dialog('open');
						e.preventDefault();
					});
			} else {
				// Add Buttons
				actionButton(options.submitText || 'Save')
					.click(function (e) {
						submitUrl(this);
						e.preventDefault();
					});
				if(!matchingStatement()) {
					if (options.saveAndClose !== false)
						actionButton((options.submitText || 'Save') + ' and Close')
							.addClass('goback')
							.click(function (e) {
								submitUrl(this);
								e.preventDefault();
							});
					if (options.saveAndNew)
						actionButton((options.submitText || 'Save') + ' and New')
							.addClass('new')
							.click(function (e) {
								submitUrl(this);
								e.preventDefault();
							});
				}
				actionButton('Reset')
					.click(function () {
						window.location.reload();
					});
			}
		}
		if(deleteUrl) {
			deleteButton = actionButton('Delete')
				.click(function (e) {
					if(confirm("Are you sure you want to delete this record"))
						deleteUrl(this);
					e.preventDefault();
				});
		}
	}
	result.fields = columns;
	result.settings = options;
	result.dataReady = dataReady;
	result.draw = draw;
	if(options.data)
		dataReady(options.data);
	else if(options.ajax) {
		get(options.ajax.url, null, dataReady);
	}
	return result;
}

/**
 * Make a header and detail form
 * @param headerSelector
 * @param detailSelector
 * @param options - has header and detail objects for the 2 parts of the form
 */
function makeHeaderDetailForm(headerSelector, detailSelector, options) {
	var submitUrl = options.submit;
	var tableName = options.header.table;
	if(submitUrl === undefined) {
		submitUrl = defaultUrl('Post');
	}
	if(typeof(submitUrl) == 'string') {
		var submitHref = submitUrl;
		submitUrl = function(button) {
			var hdg = null;
			try {
				// Validate everything
				_.each(options.header.columns, function (col) {
					if (col.inputValue) {
						hdg = col.heading;
						col.inputValue(result.header.find('#r0c' + col.name), result.header.data);
					}
				});
				_.each(options.detail.columns, function(col) {
					if(col.inputValue) {
						hdg = col.heading;
						_.each(result.detail.data, function (row, index) {
							col.inputValue(result.detail.find('#r' + index + 'c' + col.name), row);
						});
					}
				});
			} catch(e) {
				message(hdg + ':' + e);
				return;
			}
			if(options.header.validate) {
				var msg = options.header.validate();
				message(msg);
				if(msg) return;
			}
			if(options.validate) {
				msg = options.validate();
				message(msg);
				if(msg) return;
			}
			postJson(submitHref, {
				header: result.data.header,
				detail: result.data.detail
				}, function(d) {
					if($(button).hasClass('goback'))
						goback();
					else if($(button).hasClass('new'))
						window.location = urlParameter('id', 0);
					else if(tableName && d.id)
						window.location = urlParameter('id', d.id);
				});
		}
	}
	if(options.header.submit === undefined)
		options.header.submit = submitUrl;
	if(options.header.ajax === undefined || options.detail.ajax === undefined) {
		if (options.data) {
			if(options.header.ajax === undefined)
				options.header.ajax = null;
			if(options.detail.ajax === undefined)
				options.detail.ajax = null;
		} else {
			_setAjaxObject(options, 'Data', 'detail');
			if (options.ajax) {
				get(options.ajax.url, null, dataReady);
			}
		}
	}
	function dataReady(d) {
		result.data = d;
		if (!options.header.data && !options.header.ajax)
			result.header.dataReady(options.data.header);
		if (!options.detail.data && !options.detail.ajax)
			result.detail.dataReady(options.data.detail);
	}
	var result = {
		header: makeForm(headerSelector, options.header),
		detail: makeListForm(detailSelector, options.detail),
		data: options.data
	};
	result.detail.header = result.header;
	if(!matchingStatement())
		nextPreviousButtons(result.data);
	result.detail.bind('changed.field', function() {
		$('button#Back').text('Cancel');
	});
	if(options.data)
		dataReady(options.data);
	return result;
}

/**
 * Make the detail part of a header detail form
 * @param selector
 * @param options
 */
function makeListForm(selector, options) {
	var table = $(selector);
	var tableName = myOption('table', options);
	var idName = myOption('id', options, 'id' + tableName);
	var submitUrl = myOption('submit', options);
	var selectUrl = myOption('select', options);
	if(selectUrl === undefined && submitUrl === undefined) {
		submitUrl = defaultUrl('Post');
	}
	if(typeof(submitUrl) == 'string') {
		var s = submitUrl;
		//noinspection JSUnusedAssignment,JSUnusedLocalSymbols
		submitUrl = function(button) {
			try {
				var hdg;
				_.each(options.columns, function(col) {
					if(col.inputValue) {
						hdg = col.heading;
						_.each(table.data, function (row, index) {
							col.inputValue(result.find('#r' + index + 'c' + col.name), row);
						});
					}
				});
			} catch(e) {
				message(e);
				return;
			}
			if(options.validate) {
				var msg = options.validate();
				message(msg);
				if(msg) return;
			}
			postJson(s, table.data);
		}
	}
	if(typeof(selectUrl) == 'string') {
		var sel = selectUrl;
		selectUrl = function(row) {
			goto(sel + '?id=' + row[idName]);
		}
	}
	if(typeof(selectUrl) == 'function') {
		selectClick(selector, function () {
			return selectUrl.call(this, table.rowData($(this)))
		});
	} else {
		selectClick(selector, null);
	}
	$(selector).addClass('form');
	$(selector).addClass('listform');
	_setAjaxObject(options, 'Listing', '');
	var row = null;
	var columns = {};
	var heading = table.find('thead');
	if(heading.length == 0) heading = $('<thead></thead>').appendTo(table);
	var body = table.find('tbody');
	if(body.length == 0) body = $('<tbody></tbody>').appendTo(table);
	var rowsPerRecord = 0;
	var colCount = 0;
	var c = 0;
	var skip = 0;
	_.each(options.columns, function(col, index) {
		options.columns[index] = col = _setColObject(col, tableName, index);
		if(!row || col.newRow) {
			row = $('<tr></tr>').appendTo(heading);
			row.addClass("r" + ++rowsPerRecord);
			c = 0;
		}
		c++;
		if(skip) {
			skip--;
		} else {
			var cell = $('<th></th>').appendTo(row).text(col.heading).attr('title', col.hint);
			if (col.colspan) {
				cell.attr('colspan', col.colspan);
				skip = col.colspan - 1;
			}
			if (col.sClass)
				cell.attr('class', col.sClass);
		}
		columns[col.name] = col;
		col.index = index;
		colCount = Math.max(colCount, c);
	});
	if(options.deleteRows && rowsPerRecord == 1)
		$('<th></th>').appendTo(row);
	$('body').off('change', selector + ' :input');
	$('body').on('change', selector + ' :input', function() {
		var col = table.fields[$(this).attr('data-col')];
		if(col) {
			var rowIndex = table.rowIndex(this);
			var val;
			try {
				//noinspection JSCheckFunctionSignatures
				val = col.inputValue(this, table.data[rowIndex]);
			} catch(e) {
				message(col.heading + ':' + e);
				$(this).focus();
				return;
			}
			if(table.triggerHandler('changed.field', [val, table.data[rowIndex], col, this]) !== false) {
				if(this.type == 'file') {
					var img = $(this).prev('img');
					var submitHref = defaultUrl('Upload');
					var d = new FormData();
					for(var f = 0; f < this.files.length; f++)
						d.append('file' + (f || ''), this.files[f]);
					if(table.header)
						d.append('header', JSON.stringify(table.header.data));
					d.append('detail', JSON.stringify(table.data[rowIndex]));
					postFormData(submitHref, d, function(d) {
						if(tableName && d.id)
							window.location = urlParameter('id', d.id);
					});
				} else {
					table.data[rowIndex][col.data] = val;
					row = body.find('tr:eq(' + (rowIndex * rowsPerRecord) + ')');
					var cell = row.find('td:first');
				}
				_.each(options.columns, function(c) {
					if(c.newRow) {
							row = row.next('tr');
							cell = row.find('td:first');
						}
					if(c.data == col.data) {
						c.update(cell, val, rowIndex, table.data[rowIndex]);
					}
					cell = cell.next('td');
				});
			}
		}
	});
	/**
	 * Draw an individual row by index
	 * @param rowIndex
	 */
	function drawRow(rowIndex) {
		var row = null;
		var cell = null;
		var rowData = table.data[rowIndex];
		var rowno = 1;
		function newRow(r) {
			if(r.length == 0) {
				r = $('<tr></tr>').appendTo(body);
			}
			row = r;
			if(rowData["@class"])
				row.addClass(rowData["@class"]);
			row.addClass("r" + rowno++);
			cell = row.find('td:first');
		}
		newRow(body.find('tr:eq(' + (rowIndex * rowsPerRecord) + ')'));
		_.each(options.columns, function (col) {
			if(col.newRow)
				newRow(row.next('tr'));
			if(cell.length == 0) {
				cell = $('<td></td>').appendTo(row);
				if(col.sClass)
					cell.attr('class', col.sClass);
			}
			var data = rowData[col.data];
			col.update(cell, data, rowIndex, rowData);
			cell = cell.next('td');
		});
		if(options.deleteRows && rowsPerRecord == 1) {
			if(cell.length != 0)
				cell.remove();
			cell = $('<td class="deleteButton"></td>').appendTo(row);
			$('<button class="deleteButton"><img src="/images/close.png" /></button>').appendTo(cell).click(function() {
				var row = $(this).closest('tr');
				var index = row.index();
				var callback;
				if(typeof(options.deleteRows) == "function")
						callback = options.deleteRows.call(row, table.data[index]);
				if(callback != false) {
					unsavedInput = true;
					$('button#Back').text('Cancel');
					row.remove();
					table.data.splice(index, 1);
					if(typeof(callback) == 'function')
						callback();
				}
			});
		}
	}

	/**
	 * Draw the whole form
	 */
	function draw() {
		for(var row = 0; row < table.data.length; row++) {
			drawRow(row);
		}
	}
	function dataReady(d) {
		table.data = d;
		body.find('tr').remove();
		draw();
	}

	/**
	 * Redraw
	 */
	function refresh() {
		if(options.data)
			dataReady(options.data);
		else if(options.ajax) {
			body.html('<tr><td colspan="' + colCount + '" style="text-align: center;">Loading...</td></tr>');
			get(options.ajax.url, null, dataReady);
		}
	}
	refresh();
	table.fields = columns;
	table.settings = options;
	table.dataReady = dataReady;
	table.draw = draw;
	table.refresh = refresh;
	/**
	 * Return the row index of item r
	 * @param r
	 * @returns {number}
	 */
	table.rowIndex = function(r) {
		r = $(r);
		if(r.attr('tagName') != 'TR')
			r = r.closest('tr');
		return Math.floor(r.index() / rowsPerRecord);
	};
	/**
	 * Return the row data for item r
	 * @param r
	 * @returns {*}
	 */
	table.rowData = function(r) {
		return table.data[table.rowIndex(r)];
	};
	/**
	 * Draw the row
	 * @param {number|jElement} r rowIndex or item in a row
	 */
	table.drawRow = function(r) {
		if(typeof(r) != 'number')
			r = table.rowIndex(r);
		draw(r);
	};
	/**
	 * Add a new row
	 * @param row The data to add
	 */
	table.addRow = function(row) {
		table.data.push(row);
		drawRow(table.data.length - 1);
	};
	/**
	 * The cell for a data item
	 * @param rowIndex
	 * @param col The col object
	 * @returns {*|{}}
	 */
	table.cellFor = function(rowIndex, col) {
		var row = body.find('tr:eq(' + (rowIndex * rowsPerRecord) + ')');
		var cell = row.find('td:first');
		for(var c = 0; c < options.columns.length; c++) {
			if(options.columns[c].newRow) {
				row = row.next();
				cell = row.find('td:first');
			}
			if(options.columns[c] == col)
				return cell;
			cell = cell.next();
		}
	};
	return table;
}

/**
 * Extract a named option from opts, remove it, and return it (or defaultValue if not present)
 * @param {string} name
 * @param {*} opts
 * @param {string} [defaultValue]
 * @returns {*}
 */
function myOption(name, opts, defaultValue) {
	var result = opts[name];
	if(result === undefined) {
		result = defaultValue;
	} else {
		if(typeof(result) != 'function') result = _.clone(result);
		delete opts[name];
	}
	return result;
}

/**
 * Add next and previous buttons to a document display
 * @param {number} record.next id of next record
 * @param {number} record.previous id of previous record
 */
function nextPreviousButtons(record) {
	if(record && record.previous != null) {
		actionButton('Previous')
			.click(function() {
				window.location = urlParameter('id', record.previous);
			});
	}
	if(record && record.next != null) {
		actionButton('Next')
			.click(function() {
				window.location = urlParameter('id', record.next);
			});
	}
}

/**
 * Post data to url
 * @param {string} url
 * @param data
 * @param {function} [success]
 */
function postJson(url, data, success) {
	if(typeof(data) == 'function') {
		success = data;
		data = {};
	}
	if(data == null)
		data = {};
	postData(url, { json: JSON.stringify(data) }, false, success);
}

/**
 * Post form data containing uploaded file
 * @param {string} url
 * @param data
 * @param {function} [success]
 */
function postFormData(url, data, success) {
	postData(url, data, true, success, 60000);
}

/**
 * Post data
 * @param {string} url
 * @param data
 * @param {boolean} asForm true to post as multiplart/form-data (uploaded file)
 * @param {function} [success]
 * @param {number} [timeout] in msec
 */
function postData(url, data, asForm, success, timeout) {
	message(timeout > 10000 ? 'Please wait, uploading data...' : 'Please wait...');
	var ajax = {
		url: url,
		type: 'post',
		data: data,
		timeout: timeout || 10000,
		xhrFields: {
			withCredentials: true
		}
	};
	if(asForm) {
		ajax.enctype = 'multipart/form-data';
		ajax.processData = false;
		ajax.contentType = false;
	}
	$.ajax(ajax)
		.done(
		/**
		 * @param {string} [result.error] Error message
		 * @param {string} [result.message] Info message
		 * @param {string} [result.confirm] Confirmation question
		 * @param {string} [result.redirect] Where to go now
		 */
		function(result) {
			if(result.error)
				message(result.error);
			else {
				message(result.message);
				if(result.confirm) {
					// C# code wants a confirmation
					if(confirm(result.confirm)) {
						url += /\?/.test(url) ? '?' : '&';
						url += 'confirm';
						postData(url, data, asForm, success, timeout);
					}
					return;
				}
				unsavedInput = false;
				if(success && !result.redirect) {
					success(result);
					return;
				}
			}
			if(result.redirect)
				window.location = result.redirect;
		})
		.fail(function(jqXHR, textStatus, errorThrown) {
			message(textStatus == errorThrown ? textStatus : textStatus + ' ' + errorThrown);
		});
}

/**
 * Round a number to 2 decimal places
 * @param {number} v
 * @returns {number}
 */
function round(v) {
	return Math.round(100 * v) / 100;
}

/**
 * Add selected class to just this row
 */
function selectOn() {
	$(this).siblings('tr').removeClass('selected');
	$(this).addClass('selected');
}

/**
 * Remove selected class from this row
 */
function selectOff() {
	$(this).removeClass('selected');
}

/**
 * Add mouse handlers for table rows
 * @param {string} selector table
 * @param {function} selectFunction (returns false if row can't be selected)
 */
function selectClick(selector, selectFunction) {
	$('body').off('click', selector + ' tbody td:not(:has(input))');
	if(!touchScreen) {
		$('body').off('mouseenter', selector + ' tbody tr')
			.off('mouseleave', selector + ' tbody tr');
	}
	if(!selectFunction)
		return;
	var table = $(selector);
	table.addClass('noselect');
	table.find('tbody').css('cursor', 'pointer');
	$('body').on('click', selector + ' tbody td:not(:has(input))', function(e) {
		if(e.target.tagName == 'A')
			return;
		var row = $(this).closest('tr');
		// On touch screens, tap something once to select, twice to open it
		// On ordinary screens, click once to open (mouseover selects)
		var select = !touchScreen || row.hasClass('selected');
		selectOn.call(row);
		if(select && selectFunction.call(this, e) == false)
			selectOff.call(row);
		e.preventDefault();
		e.stopPropagation();
		return false;
	});
	if(!touchScreen) {
		// Mouse over highlights row
		$('body').on('mouseenter', selector + ' tbody tr', selectOn)
			.on('mouseleave', selector + ' tbody tr', selectOff);
	}
}

/**
 * Add defaultSuffix to the current url
 * @param {string} defaultSuffix
 * @returns {string}
 */
function defaultUrl(defaultSuffix) {
	var url = window.location.pathname.replace(/\.html$/, '');
	if(url.substr(1).indexOf('/') < 0)
		url += '/default';
	return url + defaultSuffix + ".html" + window.location.search
}

/**
 * Change (or add or delete) the value of a named parameter in a url
 * @param {string} name of parameter
 * @param {string|number} [value] new value (null or missing to delete)
 * @param {string} [url] If missing, use current url with any message removed
 * @returns {string}
 */
function urlParameter(name, value, url) {
	if(url === undefined)
		{ //noinspection JSCheckFunctionSignatures
			url = urlParameter('message', null, window.location.href);
		}
	var regex = new RegExp('([\?&])' + name + '(=[^\?&]*)?');
	if(value === null || value === undefined) {
		var m = regex.exec(url);
		if(m)
			url = url.replace(regex, m[1] == '?' ? '?' : '').replace('?&', '?');
	} else if(regex.test(url))
		url = url.replace(regex, '$1' + name + '=' + value);
	else
		url += (url.indexOf('?') < 0 ? '?' : '&') + name + '=' + value;
	return url;
}

/**
 * If options.data is not present, set options.ajax to retrieve the data
 * @param options
 * @param {string} defaultSuffix to add to current url if options.ajax is undefined
 * @param {string} defaultDataSrc Element in returned data to use for form data (or '')
 * @private
 */
function _setAjaxObject(options, defaultSuffix, defaultDataSrc) {
	if(typeof(options.ajax) == 'string') {
		options.ajax = {
			url: options.ajax
		}
	} else if(options.ajax === undefined && !options.data) {
		options.ajax = {
			url: defaultUrl(defaultSuffix)
		}
	}
	if(options.ajax && typeof(options.ajax) == 'object' && options.ajax.dataSrc === undefined) {
		options.ajax.dataSrc = defaultDataSrc;
	}
}

//noinspection JSUnusedLocalSymbols
/**
 * Default inputValue function for a column that doesn't have one
 * @param {jElement} field
 * @param {*} row
 * @returns {string}
 */
function getValueFromField(field, row) {
	return $(field).val();
}

/**
 * Standard update function for a column
 * @param {string} selector to find the input field in the cell
 * @param {jElement} cell
 * @param data
 * @param {number} rowno
 * @param col
 * @param row
 */
function colUpdate(selector, cell, data, rowno, col, row) {
	var i = cell.find(selector);
	if(i.length && i.attr('id')) {
		// Field exists
		i.val(data);
	} else {
		// No field yet - draw one
		cell.html(col.draw(data, rowno, row));
	}
}

//noinspection JSUnusedLocalSymbols
/**
 * Default render function for a column that doesn't have one
 * @param data
 * @param {string} type
 * @param row
 * @param {*} meta
 * @param {number} meta.row
 * @param {number} meta.col
 * @param {Array} meta.settings.oInit.columns
 * @returns {string}
 */
function colRender(data, type, row, meta) {
	switch(type) {
		case 'display':
		case 'filter':
			var col = meta.settings.oInit.columns[meta.col];
			return col.draw(data, meta.row, row);
		default:
			return data;
	}
}

//noinspection JSUnusedLocalSymbols
/**
 * Default render function for a number
 * @param data
 * @param {string} type
 * @param row
 * @param {*} meta
 * @param {number} meta.row
 * @param {number} meta.col
 * @param {Array} meta.settings.oInit.columns
 * @returns {string}
 */
function numberRender(data, type, row, meta) {
	switch(type) {
		case 'display':
			return colRender(data, type, row, meta);
		case 'filter':
			return formatNumber(data);
		default:
			return data;
	}
}

//noinspection JSUnusedLocalSymbols,JSUnusedLocalSymbols
/**
 * Default draw function for a column that doesn't have one
 * @param data
 * @param {number} rowno
 * @param row
 * @returns {string}
 */
function colDraw(data, rowno, row) {
	return data;
}

/**
 * Set up column option defaults
 * @param col
 * @param {string} tableName
 * @param {int} index
 * @returns {*} col
 * @private
 */
function _setColObject(col, tableName, index) {
	var type;
	if(typeof(col) == 'string') {
		// Shorthand - [#/@]name[/heading]
		switch(col[0]) {
			case '#':
				type = 'decimal';
				col = col.substr(1);
				break;
			case '/':
				type = 'date';
				col = col.substr(1);
				break;
			case '@':
				type = 'email';
				col = col.substr(1);
				break;
		}
		var split = col.split('/');
		col = { data: split[0] };
		if(split.length > 1) col.heading = split[1];
	} else {
		type = myOption('type', col);
	}
	if (type) _.defaults(col, Type[type]);
	if(col.attributes == null)
		col.attributes = '';
	if(typeof(col.defaultContent) == "function") {
		col.defaultContent = col.defaultContent(index, col);
	}
	if(!col.name) col.name = col.data.toString();
	if(col.heading === undefined) {
		var title = col.name;
		// Remove table name from front
		if(tableName && title.indexOf(tableName) == 0 && title != tableName)
			title = title.substr(tableName.length);
		// Split "CamelCase" name into "Camel Case", and remove Id from end
		title = title.replace(/Id$/, '').replace(/([A-Z])(?=[a-z0-9])/g, " $1");
		col.heading = title;
	}
	if(col.inputValue === undefined)
		col.inputValue = getValueFromField;
	if(col.render === undefined && col.draw)
		col.render = colRender;		// Render function for dataTable
	if(col.draw === undefined)
		col.draw = colDraw;
	if(!col.update)
		col.update = function(cell, data, rowno, row) {
			cell.html(this.draw(data, rowno, row));
		};
	return col;
}

/**
 * Get a url
 * @param {string} url
 * @param data
 * @param {function} success
 * @param {function} failure
 */
function get(url, data, success, failure) {
	$.ajax({
		url: url,
		type: 'get',
		data: data,
		timeout: 10000,
		xhrFields: {
			withCredentials: true
		}
	})
		.done(success)
		.fail(failure || function(jqXHR, textStatus, errorThrown) {
			message(textStatus == errorThrown ? textStatus : textStatus + ' ' + errorThrown);
		});
}

/**
 * Populate a select with options
 * @param {jElement} select
 * @param {Array} data The options array
 * @param {string} val current value of data item
 * @param {*} [col]
 * @param {boolean} [col.date] True if value to be formatted as date
 * @param {string} [col.emptyValue] Value to use if val is null or missing
 * @param {string} [col.emptyOption] Text to use if val does not match any option
 */
function addOptionsToSelect(select, data, val, col) {
	var found;
	var category;
	var optgroup = select;
	var multi = select.prop('multiple');
	var date = col && col.date;
	if(val == null)
		val = col && col.emptyValue != null ? col.emptyValue : '';
	_.each(data,
		/**
		 * Populate a select with options
		 * @param {string} opt.value the text to display
		 * @param {string?} [opt.id] the value to return (value if not supplied)
		 * @param {boolean} [opt.hide]
		 * @param {string} [opt.category] For categorised options
		 * @param {string}[opt.class] css class
		 */
		function(opt) {
		var id = opt.id;
		if(id === undefined)
			id = opt.value;
		if(opt.hide && id != val)
			return;
		var option = $('<option></option>');
		if(opt.id !== undefined)
			option.attr('value', opt.id);
		option.text(date ? formatDate(opt.value) : opt.value);
		if(id == val)
			found = true;
		if(opt.category && opt.category != category) {
			category = opt.category;
			optgroup = $('<optgroup label="' + opt.category + '"></optgroup>').appendTo(select);
		}
		if(opt.class)
			option.addClass(opt.class);
		option.appendTo(opt.category ? optgroup : select);
	});
	if(!found && !multi) {
		var option = $('<option></option>');
		if(col && col.emptyOption)
			option.text(col.emptyOption);
		option.attr('value', val);
		option.prependTo(select);
	}
	select.val(val);
}

/**
 * Make an indexed hash from an array
 * @param {Array} array
 * @param {string} [key] (default 'id')
 * @returns {{}}
 */
function hashFromArray(array, key) {
	var hash = {};
	if(key == null) key = 'id';
	_.each(array, function(value) {
		hash[value[key]] = value;
	});
	return hash;
}

/**
 * Stores the current url and datatable "Show All" parameters
 * @returns {string}
 */
function getTableUrl() {
	var dt = [];
	$('button[data-nz]').each(function() {
		dt.push($(this).attr('data-nz') == 'true' ? 1 : 0);
	});
	return urlParameter('dt', dt.toString());
}

/**
 * Build a url which stores the current url and datatable "Show All" parameters as "from"
 * @param {string} url base url to go to
 * @returns {string}
 */
function getGoto(url) {
	var current = getTableUrl();
	return urlParameter('from', encodeURIComponent(current), url);
}

/**
 * Go to url, storing the current url and datatable "Show All" parameters as "from"
 * @param url
 */
function goto(url) {
	window.location = getGoto(url);
}

/**
 * Go back to previous url
 */
function goback() {
	var from = matchingStatement() ? '/banking/statementmatching.html?id=' + getParameter('id') : getParameter('from');
	if(!from) {
		from = window.location.pathname;
		var pos = from.substr(1).indexOf('/');
		if(pos >= 0)
			from = from.substr(0, pos + 1);
	}
	window.location = from;
}

/**
 * Get the value of a url parameter
 * @param name
 * @returns {string}
 */
function getParameter(name) {
	var re = new RegExp('[&?]' + name + '=([^&#]*)');
	var m = re.exec(window.location.search);
	return m == null || m.length == 0 ? null : decodeURIComponent(m[1]);
}

//noinspection JSUnusedGlobalSymbols
/**
 * Return true if current url has named parameter
 * @param name
 * @returns {boolean}
 */
function hasParameter(name) {
	var re = new RegExp('[&?]' + name + '(=|&|$)');
	var m = re.exec(window.location.search);
	return m != null && m.length != 0;
}

/**
 * Return true if currently matching a statement
 * @returns {boolean}
 */
function matchingStatement() {
	return window.location.pathname == '/banking/statementmatch.html';
}

function tableData(selector) {
	var data = $(selector).html();
	data = data.replace("\r", "").replace("\n", ' ');
	data = data.replace(/<\/t[dh]>/ig, "\t");
	data = data.replace(/<\/tr>/ig, "\r\n");
	data = data.replace(/<[^>]*>/g, '');
	data = data.replace(/&nbsp;/ig, ' ');
	return data;
}

function download(button, data) {
	var menu = $('ul.menu');
	if(menu.length)	{
		menu.remove();
		return;
	}
	menu = $('<ul class="menu"><li id="txt">Tab-delimited</li><li id="csv">CSV</li><li id="clip">Copy to clipboard</li></ul>');
	var p = $(button).offset();
	menu.css('left', p.left + 'px').css('top', (p.top + $(button).height()) + 'px');
	menu.appendTo(body);
	var mnu = menu.menu({
		select: function (e, ui) {
			switch (ui.item[0].id) {
				case 'txt':
					downloadFile(document.title + '.txt', data);
					break;
				case 'csv':
					downloadFile(document.title + '.csv',
						data.replace(/\t/g, ','));
					break;
				case 'clip':
					/*
					document.dispatchEvent(new ClipboardEvent('copy', {
						dataType: 'text/plain',
						data: data
					}));
					*/
					var txt = $('<textarea></textarea>');
					txt.text(data);
					txt.appendTo('body');
					txt.select();
					document.execCommand('copy');
					txt.remove();
					break;
			}
			menu.remove();
		}
	});
}

function downloadFile(filename, text) {
	var element = document.createElement('a');
	element.setAttribute('href', 'data:text/plain;charset=utf-8,' + encodeURIComponent(text));
	element.setAttribute('download', filename);
	element.style.display = 'none';
	document.body.appendChild(element);
	element.click();
	document.body.removeChild(element);
}

/**
 * Add years to a date
 * @param {number} y
 */
Date.prototype.addYears = function(y) {
	this.setYear(this.getYear() + 1900 + y);
};

/**
 * Add months to a date
 * @param {number} m
 */
Date.prototype.addMonths = function(m) {
	var month = (this.getMonth() + m) % 12;
	this.setMonth(this.getMonth() + m);
	while(this.getMonth() > month)
		this.addDays(-1);
};

/**
 * Add days to a date
 * @param {number} d
 */
Date.prototype.addDays = function(d) {
	this.setDate(this.getDate() + d);
};

/**
 * Convert this date to yyyy-mm-dd format
 * @returns {string}
 */
Date.prototype.toYMD = function() {
	var y = this.getYear() + 1900;
	var m = (this.getMonth() + 101).toString().substr(1);
	var d = (this.getDate() + 100).toString().substr(1);
	return y + "-" + m + "-" + d;
};