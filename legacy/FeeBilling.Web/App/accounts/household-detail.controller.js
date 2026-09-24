(function () {
    'use strict';

    angular.module('feeBilling')
        .controller('HouseholdDetailController', HouseholdDetailController);

    HouseholdDetailController.$inject = ['$routeParams', 'api'];

    function HouseholdDetailController($routeParams, api) {
        var vm = this;
        var id = $routeParams.id;

        vm.household = null;
        vm.asOf = '2026-09-30'; // TODO: default to the last valuation date instead of quarter end
        vm.positions = {};      // keyed by account Id, loaded on expand
        vm.expanded = {};
        vm.loading = false;
        vm.error = null;

        vm.togglePositions = togglePositions;
        vm.asOfChanged = asOfChanged;
        vm.positionsTotal = positionsTotal;

        activate();

        function activate() {
            vm.loading = true;
            api.getHousehold(id).then(function (household) {
                vm.household = household;
            }, function (response) {
                if (response.status === 404) {
                    vm.error = 'Household ' + id + ' not found.';
                } else {
                    vm.error = 'Could not load household (' + response.status + ').';
                }
            }).finally(function () {
                vm.loading = false;
            });
        }

        function togglePositions(account) {
            if (vm.expanded[account.Id]) {
                vm.expanded[account.Id] = false;
                return;
            }

            vm.expanded[account.Id] = true;
            if (vm.positions[account.Id]) {
                return; // already loaded for this date
            }

            api.getPositions(account.Id, vm.asOf).then(function (positions) {
                vm.positions[account.Id] = positions;
            }, function (response) {
                vm.error = 'Could not load positions for ' + account.AccountNumber + ' (' + response.status + ').';
                vm.expanded[account.Id] = false;
            });
        }

        // Different date = different positions, so throw away what we have and collapse everything.
        function asOfChanged() {
            vm.positions = {};
            vm.expanded = {};
        }

        function positionsTotal(accountId) {
            var rows = vm.positions[accountId] || [];
            var total = 0;
            for (var i = 0; i < rows.length; i++) {
                total += rows[i].MarketValue;
            }
            return total;
        }
    }
})();
