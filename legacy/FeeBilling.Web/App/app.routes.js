(function () {
    'use strict';

    angular.module('feeBilling')
        .config(routes);

    routes.$inject = ['$routeProvider'];

    function routes($routeProvider) {
        $routeProvider
            .when('/billing', {
                templateUrl: 'App/billing/billing-review.html',
                controller: 'BillingReviewController',
                controllerAs: 'vm'
            })
            .when('/billing/runs/:runId', {
                templateUrl: 'App/billing/run-invoices.html',
                controller: 'RunInvoicesController',
                controllerAs: 'vm'
            })
            .when('/schedules', {
                templateUrl: 'App/schedules/schedule-list.html',
                controller: 'ScheduleListController',
                controllerAs: 'vm'
            })
            // '/schedules/new' has to be registered before '/schedules/:id' or ":id" matches "new".
            .when('/schedules/new', {
                templateUrl: 'App/schedules/schedule-edit.html',
                controller: 'ScheduleEditController',
                controllerAs: 'vm'
            })
            .when('/schedules/:id', {
                templateUrl: 'App/schedules/schedule-edit.html',
                controller: 'ScheduleEditController',
                controllerAs: 'vm'
            })
            .when('/accounts', {
                templateUrl: 'App/accounts/account-list.html',
                controller: 'AccountListController',
                controllerAs: 'vm'
            })
            .when('/households/:id', {
                templateUrl: 'App/accounts/household-detail.html',
                controller: 'HouseholdDetailController',
                controllerAs: 'vm'
            })
            .otherwise({
                redirectTo: '/billing'
            });
    }
})();
