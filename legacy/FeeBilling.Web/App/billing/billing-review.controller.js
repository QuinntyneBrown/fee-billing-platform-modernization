(function () {
    'use strict';

    angular.module('feeBilling')
        .controller('BillingReviewController', BillingReviewController);

    BillingReviewController.$inject = ['$interval', 'api'];

    function BillingReviewController($interval, api) {
        var vm = this;
        var poller = null;

        // TODO FB-204: load these from an api/firms endpoint. Only two firms on this server.
        vm.firms = [
            { id: 1, name: 'Maple Ridge Wealth Partners' },
            { id: 2, name: 'Groupe Financier Laurentien' }
        ];
        vm.firmId = (window.feeBillingConfig && window.feeBillingConfig.defaultFirmId) || 1;
        vm.periodEnd = '2026-09-30';
        vm.runs = [];
        vm.loading = false;
        vm.error = null;
        vm.message = null;

        vm.loadRuns = loadRuns;
        vm.startRun = startRun;
        vm.statusClass = statusClass;

        loadRuns();

        function loadRuns() {
            vm.loading = true;
            vm.error = null;
            return api.getRuns(vm.firmId).then(function (runs) {
                vm.runs = runs;
                checkPolling();
            }, function (response) {
                vm.error = 'Could not load billing runs (' + response.status + ').';
            }).finally(function () {
                vm.loading = false;
            });
        }

        function startRun() {
            vm.error = null;
            vm.message = null;

            // FB-247: people double-click this and we get two runs for the same period.
            // Server-side de-dupe is on the backlog.
            api.startRun(vm.firmId, vm.periodEnd).then(function (response) {
                console.log('billing run queued', response);
                vm.message = 'Billing run #' + response.runId + ' queued.';
                loadRuns();
            }, function (response) {
                vm.error = (response.data && response.data.Message) || 'Could not start the billing run.';
            });
        }

        // Poll every 5s while anything is Pending/Running so the status updates by itself.
        function checkPolling() {
            var active = vm.runs.some(function (run) {
                return run.Status === 'Pending' || run.Status === 'Running';
            });

            if (active && !poller) {
                poller = $interval(refresh, 5000);
            } else if (!active && poller) {
                $interval.cancel(poller);
                poller = null;
            }
        }

        // Same as loadRuns but doesn't flip vm.loading, otherwise the table flickers every 5s.
        function refresh() {
            api.getRuns(vm.firmId).then(function (runs) {
                vm.runs = runs;
                checkPolling();
            });
        }

        function statusClass(status) {
            switch (status) {
                case 'Complete': return 'label-success';
                case 'Running': return 'label-info';
                case 'Failed': return 'label-danger';
                default: return 'label-default';
            }
        }

        // NOTE: the poller is not cancelled when you leave the page. It keeps calling
        // api/billing/runs every 5s in the background until the run finishes. (FB-262)
    }
})();
