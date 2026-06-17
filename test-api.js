const fetch = require('node-fetch'); // wait, fetch is native in newer node

fetch('http://localhost:5073/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'employee@company.com', password: 'password123', role: 'employee' })
})
.then(res => res.json().then(data => ({ status: res.status, data })))
.then(console.log)
.catch(console.error);
