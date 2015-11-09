/**
 * Created by Nikki on 06/11/2015.
 */
function makeForm(selector, options) {
	//...
	$('body').on('change', 'mytable :input', function() {
		// Inspector complains "Argument type makeForm is not assignable to parameter type jElement"
		inputValue(this);
	});
	//...
}

/**
 * inputValue function
 * @param {jElement} field
 * @returns {string}
 */
function inputValue(field) {
	//...
	return '';
}

makeForm('', '');