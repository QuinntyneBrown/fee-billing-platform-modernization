(function () {
    'use strict';

    angular.module('feeBilling')
        .controller('ScheduleListController', ScheduleListController);

    ScheduleListController.$inject = ['api'];

    function ScheduleListController(api) {
        var vm = this;

        vm.schedules = [];
        vm.showInactive = false;
        vm.loading = false;
        vm.error = null;

        vm.load = load;
        vm.isVisible = isVisible;

        load();

        function load() {
            vm.loading = true;
            vm.error = null;
            api.getSchedules().then(function (schedules) {
                vm.schedules = schedules;
            }, function (response) {
                vm.error = 'Could not load fee schedules (' + response.status + ').';
            }).finally(function () {
                vm.loading = false;
            });
        }

        // used as an ng-repeat filter predicate
        function isVisible(schedule) {
            return vm.showInactive || schedule.IsActive;
        }
    }
})();
