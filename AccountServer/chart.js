var chartColours = [];
chartColour = function(h, s, v) {
	h *= 360;
	var R, G, B, X, C;
	h = (h % 360) / 60;
	C = v * s;
	X = C * (1 - Math.abs(h % 2 - 1));
	R = G = B = v - C;

	h = ~~h;
	R += [C, X, 0, 0, X, C][h];
	G += [X, C, C, X, 0, 0][h];
	B += [0, 0, X, C, C, X][h];
	R *= 255;
	G *= 255;
	B *= 255;
    return {
    	r: R, 
    	g: G, 
    	b: B,
    	hex: "#" + (16777216 | Math.round(B) | (Math.round(G) << 8) | (Math.round(R) << 16)).toString(16).slice(1)
    	};
}
var hue = 0,
	hueDelta = 0.618033988749895,
	minDiff = 10,
	rejectCount = 0,
	white = new chartColour(0, 0, 1);
function getColours(length) {
	function colourDiff(colour1, colour2) {
		return Math.round((Math.abs(colour1.r - colour2.r) * 299 + Math.abs(colour1.g - colour2.g) * 587 + Math.abs(colour1.b - colour2.b) * 114) / 1000);
	}
	while(chartColours.length < length) {
		hue += hueDelta;
		if (hue > 1) hue -= 1;
		var colour = new chartColour(hue, 0.60, 0.95);
		if(colourDiff(white, colour) < 35)
			continue;
		var wanted = true;
		$.each(chartColours, function() {
			if(colourDiff(colour, this) < minDiff) {
				if(rejectCount++ > 10) {
					rejectCount = 0;
					minDiff /= 2;
				}
				wanted = false;
				return false;
			}
		});
		if(wanted)
			chartColours.push(colour);
	}
	return chartColours.slice(0, length).map(function(c) { return c.hex; });
}
/**
 * Set up a chart
 * @param {object} record Chart info
 * @param {function} select Function to call when clicking on a chart item
 * @param {function} [update] Function to call after loading chart data
 * @returns {*} record
 */
function initialiseChart(record, select, update) {
	var chart;
	var fields;
	var filters = record.filters;
	var originalName = record.settings.ReportName;

	$(window).bind('beforeprint', function() { 
		if(chart)
			chart.resize(); 
	});
	/**
	 * Display the chart
	 * @param {object} record incoming data
	 */
	function loadData(record) {
		var colours = getColours(Math.max(record.chart.datasets.length, record.chart.labels.length));
		fields = record.settings.fields;
		// Make the chart itself
		$.each(record.chart.datasets, function(index) {
			switch(record.settings.parameters.ChartType) {
				case 'pie':
				case 'doughnut':
					this.backgroundColor = colours;
					break;
				default:
					this.backgroundColor = colours[index];
					break;
			}
		});
		var chartOptions = {
			type: record.settings.parameters.ChartType,
			data: record.chart,
			options: {
				responsive: true,
				legend: {
					position: 'top',
				},
				title: {
					display: true,
					text: record.settings.ReportName
				},
				tooltips: {
					callbacks: {
						title: function(tooltipItems, data) { 
							return data.datasets[tooltipItems[0].datasetIndex].label;
        				},
						label: function(tooltipItem, data) { 
							return data.labels[tooltipItem.index] + ": " + formatNumberWithCommas(data.datasets[tooltipItem.datasetIndex].data[tooltipItem.index]);
        				}
					}
				}
			}
		};
		switch(chartOptions.type) {
			case 'line':
			case 'bar':
			case 'horizontalBar':
				chartOptions.options.scales = {
					xAxes: [{
						ticks: {
							autoSkip: record.dateorder
						}
					}],
					yAxes: [{
					}]
				};
				break;
			case 'stackedbar':
			case 'stackedline':
				chartOptions.type = chartOptions.type.substr(7);
				chartOptions.options.scales = {
					xAxes: [{
						stacked: true,
						ticks: {
							autoSkip: record.dateorder
						}
					}],
					yAxes: [{
						stacked: true,
					}]
				};
				break;
		}
		if(chart)
			chart.destroy();
		chart = new Chart($('#chart'), chartOptions);
		// Filter form
		chart.filterForm = makeForm('#filters', {
			data: record.settings.filters,
			submit: null,
			readonly: record.readonly,
			columns: _.map(filters, _.clone)
		});
		// Chart name
		chart.nameForm = makeForm('#names', {
			data: record.settings,
			submit: null,
			readonly: record.readonly,
			columns: [
				{
					data: 'ReportName',
					heading: 'Chart Name',
					type: 'textInput'
				}
			]
		});
		chart.optionsForm = makeForm('#options', {
			data: record.settings.parameters,
			submit: null,
			readonly: record.readonly,
			columns: [
				{
					data: 'Y',
					heading: 'Value to plot',
					type: 'selectInput',
					selectOptions: record.settings.fields
				},
				{
					data: 'SortByValue',
					heading: 'Sort by value',
					type: 'checkboxInput'
				},
				{
					data: 'X1',
					heading: 'Items to plot',
					type: 'selectInput',
					selectOptions: record.settings.x1Options
				},
				{
					data: 'X2',
					heading: 'Items to plot (level 2)',
					type: 'selectInput',
					selectOptions: record.settings.x2Options
				},
				{
					data: 'ChartType',
					type: 'selectInput',
					selectOptions: [
						{ id: 'line', value: 'Line' },
						{ id: 'stackedline', value: 'Stacked Line' },
						{ id: 'bar', value: 'Vertical Bar' },
						{ id: 'horizontalBar', value: 'Horizontal Bar' },
						{ id: 'stackedbar', value: 'Stacked Bar' },
						{ id: 'doughnut', value: 'Doughnut' },
						{ id: 'pie', value: 'Pie' },
						{ id: 'radar', value: 'Radar' }
					]
				}
			]
		});
		if(update)
			update(record, chart);
	}

	/**
	 * Reload the chart with any new settings
	 */
	function refresh() {
		var modified = unsavedInput;
		postJson(defaultUrl('Save'), record.settings, function(data) {
			record = data;
			$('#settings table thead,#settings table tbody').remove();
			loadData(record);
			unsavedInput = modified;
		});
	}

	loadData(record);

	// Chart settings will be a dialog
	var dialog = $('#settingsDialog').dialog({
		autoOpen: false,
		modal: true,
		title: 'Chart settings',
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
	actionButton(record.readonly ? 'View chart settings' : 'Modify chart').click(function() {
		dialog.dialog('open');
	});
	actionButton('Refresh').click(refresh);
	if(!record.readonly) {
		actionButton('Memorise').click(function () {
			function postIt() {
				postJson('/charts/savechart.html', record.settings, function (r) {
					record.settings.idReport = r.id;
					originalName = record.settings.ReportName;
				});
			}

			if (record.settings.idReport && record.settings.ReportName != originalName) {
				var dlg = $('<div>Create another copy of the chart<br />or just update the existing one</div>')
					.appendTo($('body'))
					.dialog({
						autoOpen: true,
						modal: true,
						title: 'Memorising chart',
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
				if (confirm('Delete this memorised chart'))
					postJson('/charts/DeleteChart?id=' + record.settings.idReport, function () {
						goback();
					});
			});
	}
	actionButton('Print').click(function() {
		window.print();
	});
	return record;
}

