window.sagaLogFunctions = {
    toggleSagaDisplay: function (sagaId) {
        const contentElement = document.getElementById('saga-content-' + sagaId);
        const headerElement = document.getElementById('saga-header-' + sagaId);
        const chevronElement = document.getElementById('chevron-' + sagaId);

        if (!contentElement || !headerElement || !chevronElement) return;

        if (contentElement.style.display === 'none') {
            // Show content
            contentElement.style.display = 'block';
            headerElement.classList.add('active');
            chevronElement.classList.remove('rotated');
        } else {
            // Hide content
            contentElement.style.display = 'none';
            headerElement.classList.remove('active');
            chevronElement.classList.add('rotated');
        }
    },

    initializeSagaToggles: function () {
        // First, analyze the content to properly identify saga types
        this.analyzeSagaContent();

        // Initialize all toggle elements
        document.querySelectorAll('[data-saga-toggle]').forEach((element, index) => {
            if (!element.hasAttribute('data-initialized')) {
                const sagaId = element.getAttribute('data-saga-toggle');

                // Update the numbering to be sequential
                const numberBadge = element.querySelector('.saga-number-badge');
                if (numberBadge) {
                    numberBadge.textContent = '#' + (index + 1);
                }

                element.addEventListener('click', function () {
                    window.sagaLogFunctions.toggleSagaDisplay(sagaId);
                });

                element.setAttribute('data-initialized', 'true');
            }
        });

        // Add a class to all chevron icons for easier styling
        document.querySelectorAll('[id^="chevron-"]').forEach(icon => {
            icon.classList.add('chevron-icon');
        });
    },

    analyzeSagaContent: function () {
        document.querySelectorAll('.saga-content').forEach(content => {
            let sagaType = "Operation Log";
            let badgeColor = "bg-gray-500";

            // Determine the saga type by inspecting the content
            const htmlContent = content.innerHTML;

            if (htmlContent.includes("[CancelBooking]") ||
                htmlContent.includes("[CancelInventoryReserving]") ||
                htmlContent.includes("[Refund]")) {
                sagaType = "Cancellation Saga";
                badgeColor = "bg-red-500";
            } else if (htmlContent.includes("[CarBooking]") ||
                htmlContent.includes("[InventoryReserving]") ||
                htmlContent.includes("[Billing]")) {
                sagaType = "Reservation Saga";
                badgeColor = "bg-blue-500";
            } else if (htmlContent.includes("[Billing]") &&
                (htmlContent.includes("Billing Success") ||
                    htmlContent.includes("passed validation successfully"))) {
                sagaType = "Billing Process";
                badgeColor = "bg-green-500";
            }

            // Find corresponding header
            const sagaId = content.id.replace('saga-content-', '');
            const header = document.getElementById('saga-header-' + sagaId);

            if (header) {
                const titleSpan = header.querySelector('.saga-title');
                const badge = header.querySelector('.saga-badge');

                if (titleSpan) {
                    titleSpan.textContent = sagaType;
                }

                if (badge) {
                    // Remove all existing color classes
                    badge.className = badge.className.replace(/bg-\w+-\d+/g, '').trim();
                    // Add the new color class
                    badge.classList.add(badgeColor);
                }
            }
        });
    }
};
