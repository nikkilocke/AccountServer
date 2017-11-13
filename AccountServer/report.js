/**
 * Set up a report
 * @param {function} select Function to call when clicking on a report row
 * @param {function} [update] Function to call after loading report data
 * @returns {*}
 */
function initialiseReport(record, select, update) {
	var report;
	var fields;
	var filters = record.filters;
	var originalName = record.settings.ReportName;

	/**
	 * Select function for report row
	 * @param row Data for the whole row
	 * @returns {*}
	 */
	function selectFunction(row) {
		return select.call(this, row, record, report);
	}

	/**
	 * Display the report
	 * @param record
	 */
	function loadData(record) {
		$('#reportTitle').text(record.settings.ReportName);
		fields = record.settings.fields;
		// If there is a date filter, show it in the heading
		$('#reportDates').text('');
		for (var i = 0; i < filters.length; i++) {
			var f = filters[i];
			if (f.type == 'dateFilter') {
				var v = record.settings.filters[f.data];
				var range = _.find(dateSelectOptions, function (o) {
					//noinspection JSReferencingMutableVariableFromClosure
					return o.id == v.range;
				});
				if (v.range > 1) {
					var d = new Date(v.end);
					d.setDate(d.getDate() - 1);
					var r = range.value;
					switch(v.range) {
						case 13:
							r = v.count + ' days';
							break;
						case 14:
							r = v.count + ' months';
							break;
					}
					$('#reportDates').text(r + ': ' + formatDate(v.start) + ' - ' + formatDate(d));
				}
				break;
			}
		}
		// Make the report itself
		report = makeListForm('#report', {
			select: select == null ? null : selectFunction,
			data: record.report,
			columns: getReportColumns()
		});
		// Field selector form
		report.fieldForm = makeListForm('#fields', {
			data: fields,
			submit: null,
			readonly: record.readonly,
			sortable: true,
			columns: [
				'heading/Name',
				{
					data: 'Include',
					type: 'checkboxInput'
				}
			]
		});
		// Filter form
		report.filterForm = makeForm('#filters', {
			data: record.settings.filters,
			submit: null,
			readonly: record.readonly,
			columns: _.map(filters, _.clone)
		});
		// Report name
		report.nameForm = makeForm('#names', {
			data: record.settings,
			submit: null,
			readonly: record.readonly,
			columns: [
				{
					data: 'ReportName',
					type: 'textInput'
				}
			]
		});
		var sortOptions = record.sortOrders;
		if(sortOptions && sortOptions.length > 1) {
			// Sort options
			var sortColumns = [
					{
						data: 'sort',
						heading: 'Sort By',
						type: 'selectInput',
						selectOptions: sortOptions
					},
					{
						data: 'desc',
						heading: 'Descending order',
						type: 'checkboxInput'
					},
					{
						data: 'total',
						heading: 'Show totals',
						type: 'selectInput',
						selectOptions: [
							{ id: 0, value: "Data but no totals" },
							{ id: 1, value: "Data and totals" },
							{ id: 0, value: "Totals only" }
							]
					},
					{
						data: 'split',
						heading: 'Compact to save paper',
						type: 'checkboxInput'
					}
				];
			if(record.settings.sorting.reverseSign !== undefined)
				sortColumns.push({
					data: 'reverseSign',
					heading: 'Reverse sign of amounts',
					type: 'checkboxInput'
					});
			report.sortForm = makeForm('#sorting', {
				data: record.settings.sorting,
				table: 'Report',
				submit: null,
				readonly: record.readonly,
				columns: sortColumns
			});
		} else {
			if(record.parameters) {
				record.parameters.data = record.settings.parameters;
				record.parameters.submit = null;
				record.parameters.readonly = record.readonly;
				report.sortForm = makeForm('#sorting', record.parameters);
			} else
				$('#sorting').prev().hide()
		}
		if(/audit/i.test(window.location.pathname)) {
			// For audit reports, highlight changes
			var newRecno = -1;
			var recordUpdated;
			for(var rr = record.report.length, r = 0; r < rr; r++) {
				var rec = record.report[r];
				//noinspection FallThroughInSwitchStatementJS
				switch(rec.ChangeType) {
					case 2:
						newRecno = r;
						recordUpdated = false;
						continue;
					case 3:
						recordUpdated = true;
						if(newRecno < 0)
							continue;
						// drop through
					case undefined:
						if(!recordUpdated)
							continue;
						break;
					default:
						newRecno = -1;
						recordUpdated = false;
						continue;
				}
				var updateRec = record.report[newRecno];
				if(updateRec.ChangeType == 3) {
					updateRec = record.report[--newRecno];
				}
				_.each(report.settings.columns, function(col) {
					if(col.data != 'DateChanged') {
						if(updateRec[col.data] != rec[col.data]) {
							report.cellFor(r, col).addClass('ch');
							report.cellFor(newRecno, col).addClass('ch');
						}
					}
				});
				newRecno++;
			}
		}
		if(update)
			update(record, report);
		if($('#Download').length == 0) {
			actionButton('Download').click(function () {
				var data = downloadData(report.fields, report.data);
				download(this, data);
			});
		}
	}

	/**
	 * Extract the columns to show on the report
	 * @returns {*|Array}
	 */
	function getReportColumns() {
		return _.filter(fields, function(field) { return field.Include; }).map(function(f) {
			f = _.clone(f);	// So original isn't changed
			if(f.type == 'decimal')
				f.type = 'bracket';
			return f;
		});
	}

	/**
	 * Reload the report with any new settings
	 */
	function refresh() {
		var modified = unsavedInput;
		record.settings.fields = report.fieldForm.data;
		postJson(defaultUrl('Save'), record.settings, function(data) {
			record = data;
			$('#report thead,#report tbody,#settings table thead,#settings table tbody').remove();
			loadData(record);
			unsavedInput = modified;
		});
	}

	loadData(record);

	// Report settings will be a dialog
	var dialog = $('#settingsDialog').dialog({
		autoOpen: false,
		modal: true,
		title: 'Report settings',
		height: Math.min($('#settings').height() + 200, $(window).height() * 0.9),
		width: $('#settings').width(),
		buttons: record.readonly ? {
			Ok: {
				id: 'Ok',
				text: 'Ok',
				click: function() {
					$(this).dialog("close");
				}
			}
		} : {
			Ok: {
				id: 'Ok',
				text: 'Ok',
				click: function() {
					$(this).dialog("close");
					refresh();
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
	actionButton(record.readonly ? 'View report settings' : 'Modify report').click(function() {
		dialog.dialog('open');
	});
	actionButton('Refresh').click(refresh);
	if(!record.readonly) {
		actionButton('Memorise').click(function () {
			function postIt() {
				postJson('/reports/savereport.html', record.settings, function (r) {
					record.settings.idReport = r.id;
					originalName = record.settings.ReportName;
				});
			}

			if (record.settings.idReport && record.settings.ReportName != originalName) {
				var dlg = $('<div>Create another copy of the report<br />or just update the existing one</div>')
					.appendTo($('body'))
					.dialog({
						autoOpen: true,
						modal: true,
						title: 'Memorising report',
						buttons: {
							Copy: {
								id: 'Copy',
								text: 'Copy',
								click: function () {
									record.settings.idReport = null;
									$(this).dialog("close");
									dlg.remove();
									postIt();
								}
							},
							Overwrite: {
								id: 'Overwrite',
								text: 'Overwrite',
								click: function () {
									$(this).dialog("close");
									dlg.remove();
									postIt();
								}
							},
							Cancel: {
								id: 'Cancel',
								text: 'Cancel',
								click: function () {
									$(this).dialog("close");
									dlg.remove();
								}
							}
						}
					});
			} else
				postIt();
		});
		if (record.settings.idReport)
			actionButton('Delete').click(function () {
				if (confirm('Delete this memorised report'))
					postJson('/reports/DeleteReport?id=' + record.settings.idReport, function () {
						goback();
					});
			});
	}
	actionButton('Print').click(function() {
		window.print();
	});
	return record;
}

/**
 * A select function to create a new report with extra filters
 * @param {string} reportType to create (e.g. 'journals'
 * @param settings Existing report settings
 * @param extraFilters
 */
function postReportSettings(reportType, settings, extraFilters) {
	var url = getGoto('/reports/' + reportType + '.html?id=0');
	var newSettings = {
		ReportType: reportType,
		ReportName: settings.ReportName + " (drilldown)",
		filters: _.defaults(extraFilters || {}, settings.filters),
		sorting: { total: 1 }
	};
	$('#reportForm').remove();
	var form = $('<form id="reportForm" method="POST" action="' + url + '"><input name="json" type="hidden" value="" />');
	form.appendTo($('body'));
	form.find('input').val(JSON.stringify(newSettings));
	form[0].submit();
}

