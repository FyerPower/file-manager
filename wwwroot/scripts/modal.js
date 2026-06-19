(function () {
    // Modal Control
    const $modalOverlay = document.getElementById('modalOverlay');
    const $modal = document.getElementById('modal');
    const $btnOpenModal = document.getElementById('btnOpenModal');
    const $btnCloseModal = document.getElementById('btnCloseModal');

    $btnOpenModal.addEventListener('click', () => {
        $modalOverlay.classList.add('active');
    });

    $btnCloseModal.addEventListener('click', () => {
        $modalOverlay.classList.remove('active');
    });

    // Close modal when clicking on the overlay background
    $modalOverlay.addEventListener('click', (e) => {
        if (e.target === $modalOverlay) {
            $modalOverlay.classList.remove('active');
        }
    });

    // Close modal with Escape key
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && $modalOverlay.classList.contains('active')) {
            $modalOverlay.classList.remove('active');
        }
    });
})();