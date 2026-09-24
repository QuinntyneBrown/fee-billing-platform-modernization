(function () {
    'use strict';

    angular.module('feeBilling')
        .controller('ScheduleEditController', ScheduleEditController);

    ScheduleEditController.$inject = ['$routeParams', '$location', 'api'];

    function ScheduleEditController($routeParams, $location, api) {
        var vm = this;
        var id = $routeParams.id;

        vm.isNew = !id;
        vm.schedule = null;
        vm.scheduleTypes = ['TIERED', 'BLENDED', 'FLAT'];
        vm.currencies = ['CAD', 'USD'];
        vm.loading = false;
        vm.saving = false;
        vm.error = null;
        vm.validationErrors = [];

        vm.addTier = addTier;
        vm.removeTier = removeTier;
        vm.save = save;

        activate();

        function activate() {
            if (vm.isNew) {
                vm.schedule = {
                    Id: 0,
                    Code: '',
                    Name: '',
                    ScheduleType: 'TIERED',
                    IsHousehold: false,
                    MinimumAnnualFee: 0,
                    FlatAnnualFee: null,
                    Currency: 'CAD',
                    IsActive: true,
                    Tiers: []
                };
                addTier();
                return;
            }

            vm.loading = true;
            api.getSchedule(id).then(function (schedule) {
                vm.schedule = toForm(schedule);
            }, function (response) {
                vm.error = 'Could not load fee schedule ' + id + ' (' + response.status + ').';
            }).finally(function () {
                vm.loading = false;
            });
        }

        // The API stores AnnualRate as a fraction (0.0075) but users think in percent (0.75).
        // Multiply by 100 on load and divide by 100 on save. Keep these two in sync! (FB-256: a
        // schedule once went out at 75% because a new code path skipped the conversion.)
        function toForm(schedule) {
            angular.forEach(schedule.Tiers, function (tier) {
                tier.AnnualRate = tier.AnnualRate * 100;
            });
            return schedule;
        }

        function fromForm(schedule) {
            var dto = angular.copy(schedule);
            angular.forEach(dto.Tiers, function (tier) {
                tier.AnnualRate = tier.AnnualRate / 100;
            });
            return dto;
        }

        function addTier() {
            var tiers = vm.schedule.Tiers;
            var last = tiers.length ? tiers[tiers.length - 1] : null;
            tiers.push({
                LowerBound: last && last.UpperBound ? last.UpperBound : 0,
                UpperBound: null,
                AnnualRate: 0
            });
        }

        function removeTier(index) {
            vm.schedule.Tiers.splice(index, 1);
        }

        function save(form) {
            vm.error = null;
            vm.validationErrors = [];

            if (form && form.$invalid) {
                vm.error = 'Please fill in the required fields.';
                return;
            }

            vm.saving = true;
            var dto = fromForm(vm.schedule);
            var request = vm.isNew ? api.createSchedule(dto) : api.updateSchedule(id, dto);

            request.then(function () {
                $location.path('/schedules');
            }, function (response) {
                if (response.status === 400 && response.data && response.data.ModelState) {
                    // Web API 2 shape: { Message: "...", ModelState: { "dto.Code": ["..."] } }
                    vm.error = response.data.Message;
                    angular.forEach(response.data.ModelState, function (messages, key) {
                        angular.forEach(messages, function (message) {
                            vm.validationErrors.push({ field: key.replace(/^dto\./, ''), message: message });
                        });
                    });
                } else {
                    vm.error = (response.data && response.data.Message) || 'Save failed (' + response.status + ').';
                }
            }).finally(function () {
                vm.saving = false;
            });
        }
    }
})();
