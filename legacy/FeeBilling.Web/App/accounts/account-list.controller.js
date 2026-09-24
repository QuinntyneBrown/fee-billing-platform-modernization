(function () {
    'use strict';

    angular.module('feeBilling')
        .controller('AccountListController', AccountListController);

    AccountListController.$inject = ['api'];

    function AccountListController(api) {
        var vm = this;

        // Same list as BillingReviewController. TODO FB-204: api/firms.
        vm.firms = [
            { id: 1, name: 'Maple Ridge Wealth Partners' },
            { id: 2, name: 'Groupe Financier Laurentien' }
        ];
        vm.firmId = (window.feeBillingConfig && window.feeBillingConfig.defaultFirmId) || 1;
        vm.search = '';
        vm.accounts = [];
        vm.loading = false;
        vm.error = null;

        vm.load = load;

        load();

        function load() {
            vm.loading = true;
            vm.error = null;
            api.getAccounts(vm.firmId, vm.search).then(function (accounts) {
                vm.accounts = accounts;
            }, function (response) {
                vm.error = 'Could not load accounts (' + response.status + ').';
            }).finally(function () {
                vm.loading = false;
            });
        }
    }
})();
