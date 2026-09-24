(function () {
    'use strict';

    angular.module('feeBilling')
        .controller('RunInvoicesController', RunInvoicesController);

    RunInvoicesController.$inject = ['$routeParams', '$window', 'api'];

    function RunInvoicesController($routeParams, $window, api) {
        var vm = this;
        var runId = $routeParams.runId;

        vm.run = null;
        vm.invoices = [];
        vm.filterText = '';
        vm.totalAmount = 0;
        vm.loading = false;
        vm.approving = false;
        vm.error = null;
        vm.approvedMessage = null;
        vm.csvUrl = api.getRunInvoicesCsvUrl(runId);

        vm.pdfUrl = api.getInvoicePdfUrl;
        vm.approve = approve;

        activate();

        function activate() {
            vm.loading = true;

            api.getRun(runId).then(function (run) {
                vm.run = run;
            }, function (response) {
                vm.error = 'Could not load billing run ' + runId + ' (' + response.status + ').';
            });

            loadInvoices().finally(function () {
                vm.loading = false;
            });
        }

        function loadInvoices() {
            // The server sends back the whole DataSet, so the rows are under "Table" (FB-238).
            // If anyone names the DataTable on the server this page silently shows nothing.
            return api.getRunInvoices(runId).then(function (data) {
                vm.invoices = data.Table || [];
                vm.totalAmount = sumAmount(vm.invoices);
            }, function (response) {
                vm.error = 'Could not load invoices (' + response.status + ').';
            });
        }

        // Total for the whole run. It does NOT follow the filter box.
        function sumAmount(rows) {
            var total = 0;
            for (var i = 0; i < rows.length; i++) {
                total += rows[i].Amount;
            }
            return total;
        }

        function approve() {
            if (!$window.confirm('Approve all draft invoices in run ' + runId + '? This cannot be undone.')) {
                return;
            }

            vm.approving = true;
            vm.error = null;
            vm.approvedMessage = null;

            api.approveRun(runId).then(function (result) {
                vm.approvedMessage = result.approved + ' invoice(s) approved.';
                // Reload so the Status column shows Approved.
                return loadInvoices();
            }, function (response) {
                vm.error = (response.data && response.data.Message) || 'Approve failed (' + response.status + ').';
            }).finally(function () {
                vm.approving = false;
            });
        }
    }
})();
