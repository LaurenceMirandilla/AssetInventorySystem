// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// File size limit for every upload in the system. Any <input type="file">
// with data-max-bytes is checked as soon as a file is picked: too big, and
// the choice is cleared with a message under the box, before anything is
// sent. The server checks the same limit (Helpers/FileUploads.cs).
document.addEventListener('change', function (e) {
    var input = e.target;
    if (!input.matches || !input.matches('input[type="file"][data-max-bytes]')) return;

    var max = parseInt(input.getAttribute('data-max-bytes'), 10);
    var tooBig = Array.from(input.files || []).find(function (f) { return f.size > max; });

    var note = input.parentElement.querySelector('.file-size-error');
    if (!note) {
        note = document.createElement('div');
        note.className = 'file-size-error text-danger small mt-1';
        input.insertAdjacentElement('afterend', note);
    }

    if (tooBig) {
        // Rounded up, so a file just over the limit reads 3.1 MB, not 3 MB.
        var mb = function (bytes) { return (Math.ceil(bytes / 1024 / 1024 * 10) / 10).toFixed(1).replace(/\.0$/, ''); };
        note.textContent = '"' + tooBig.name + '" is ' + mb(tooBig.size) + ' MB. The limit is ' + mb(max) + ' MB — pick a smaller file.';
        input.value = '';
    } else {
        note.textContent = '';
    }
});
